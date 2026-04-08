using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace Coem.Cmp.Web.Services
{
    public interface IPartnerCenterSyncService
    {
        Task<int> SyncCustomersAsync();
        Task<int> SyncSubscriptionsAsync();
        Task SyncNightlyUsageAsync();
    }

    public class PartnerCenterSyncService : IPartnerCenterSyncService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDataProtector _protector;

        public PartnerCenterSyncService(ApplicationDbContext context, IHttpClientFactory httpClientFactory, IDataProtectionProvider dataProtectionProvider)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _protector = dataProtectionProvider.CreateProtector("Coem.Cmp.RegionalSecrets.v1");
        }

        // --- MÉTODOS DE SINCRONIZACIÓN DE CLIENTES Y SUSCRIPCIONES INTACTOS ---
        public async Task<int> SyncCustomersAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            if (!regionalConfigs.Any()) throw new InvalidOperationException("Bóveda Regional vacía.");

            int totalSynced = 0;
            foreach (var config in regionalConfigs)
            {
                try
                {
                    var authResult = await GetTokenAsync(config);
                    var client = CreateHttpClient(authResult.AccessToken);
                    string currentUrl = "https://api.partnercenter.microsoft.com/v1/customers";

                    do
                    {
                        var response = await client.GetAsync(currentUrl);
                        response.EnsureSuccessStatusCode();
                        var content = await response.Content.ReadAsStringAsync();
                        var pcData = JsonDocument.Parse(content);

                        foreach (var item in pcData.RootElement.GetProperty("items").EnumerateArray())
                        {
                            var pcTenantId = Guid.Parse(item.GetProperty("companyProfile").GetProperty("tenantId").GetString());
                            var existingTenant = await _context.Tenants.FirstOrDefaultAsync(t => t.MicrosoftTenantId == pcTenantId);

                            if (existingTenant == null)
                            {
                                _context.Tenants.Add(new Tenant
                                {
                                    MicrosoftTenantId = pcTenantId,
                                    Name = item.GetProperty("companyProfile").GetProperty("companyName").GetString(),
                                    DefaultDomain = item.GetProperty("companyProfile").GetProperty("domain").GetString(),
                                    AgreementType = "CSP",
                                    Country = config.CountryName,
                                    IsBilledByCoem = true,
                                    IsActive = true,
                                    OnboardingDate = DateTime.UtcNow
                                });
                            }
                            totalSynced++;
                        }
                        await _context.SaveChangesAsync();

                        currentUrl = null;
                        if (pcData.RootElement.TryGetProperty("links", out var links) && links.TryGetProperty("next", out var nextLink))
                        {
                            var uriString = nextLink.GetProperty("uri").GetString();
                            currentUrl = uriString.StartsWith("http") ? uriString : $"https://api.partnercenter.microsoft.com/{(uriString.StartsWith("/") ? uriString.Substring(1) : uriString)}";
                        }
                    } while (!string.IsNullOrEmpty(currentUrl));
                }
                catch (Exception ex) { Console.WriteLine($"[ERROR REGIONAL CUSTOMERS] {config.CountryName}: {ex.Message}"); }
            }
            return totalSynced;
        }

        public async Task<int> SyncSubscriptionsAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            int processedCount = 0;

            foreach (var config in regionalConfigs)
            {
                try
                {
                    var authResult = await GetTokenAsync(config);
                    var client = CreateHttpClient(authResult.AccessToken);
                    var tenants = await _context.Tenants.Where(t => t.Country == config.CountryName).ToListAsync();

                    foreach (var tenant in tenants)
                    {
                        string url = $"https://api.partnercenter.microsoft.com/v1/customers/{tenant.MicrosoftTenantId}/subscriptions";
                        var response = await client.GetAsync(url);
                        if (!response.IsSuccessStatusCode) continue;

                        var content = await response.Content.ReadAsStringAsync();
                        var pcData = JsonDocument.Parse(content);

                        if (!pcData.RootElement.TryGetProperty("items", out var items)) continue;

                        foreach (var item in items.EnumerateArray())
                        {
                            var subId = Guid.Parse(item.GetProperty("id").GetString());
                            var offerName = item.GetProperty("offerName").GetString();
                            var offerId = item.GetProperty("offerId").GetString();

                            // Categorización simplificada
                            string categoryTag = "Colab";
                            if (offerName.Contains("Azure plan", StringComparison.OrdinalIgnoreCase) || offerId.Contains("DZH318Z0BPS6")) categoryTag = "AP";
                            else if (offerName.Contains("Microsoft Azure", StringComparison.OrdinalIgnoreCase) || offerId.Contains("MS-AZR-0145P")) categoryTag = "AL";

                            var existingSub = await _context.Subscriptions.FirstOrDefaultAsync(s => s.Id == subId);

                            if (existingSub != null)
                            {
                                existingSub.Status = item.GetProperty("status").GetString();
                                existingSub.OfferName = offerName;
                            }
                            else
                            {
                                DateTime? effDate = item.TryGetProperty("effectiveStartDate", out var dateEl) && dateEl.ValueKind != JsonValueKind.Null ? dateEl.GetDateTime() : null;
                                _context.Subscriptions.Add(new Subscription
                                {
                                    Id = subId,
                                    TenantId = tenant.Id,
                                    OfferId = offerId,
                                    OfferName = offerName,
                                    Category = categoryTag,
                                    CreatedDate = item.GetProperty("creationDate").GetDateTime(),
                                    EffectiveDate = effDate,
                                    Status = item.GetProperty("status").GetString(),
                                    Markup = 0.00m
                                });
                            }
                            processedCount++;
                        }
                        await _context.SaveChangesAsync();
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[ERROR SYNC SUBS] {config.CountryName}: {ex.Message}"); }
            }
            return processedCount;
        }

        // --- EL NUEVO MOTOR NCE GLOBAL (CEREBRO FINANCIERO ZENITH) ---
        public async Task SyncNightlyUsageAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            var targetDate = DateTime.UtcNow.AddDays(-5).Date;

            foreach (var config in regionalConfigs)
            {
                try
                {
                    var authResult = await GetTokenAsync(config);
                    var client = CreateHttpClient(authResult.AccessToken);

                    // 1. Mapa NCE de la DB local: Solo traemos suscripciones Azure Plan (AP)
                    var tenantsIds = await _context.Tenants.Where(t => t.Country == config.CountryName).Select(t => t.Id).ToListAsync();
                    var nceSubs = await _context.Subscriptions
                        .Where(s => tenantsIds.Contains(s.TenantId) && s.Category == "AP")
                        .ToDictionaryAsync(s => s.Id, s => s);

                    if (!nceSubs.Any()) continue;

                    Console.WriteLine($"[ZENITH] Solicitando Azure Plan Global NCE para {config.CountryName}...");

                    // 2. LA LLAMADA MAESTRA: Global Daily Rated Usage (Sin ID de cliente)
                    string nceUrl = "https://api.partnercenter.microsoft.com/v1/invoices/unbilled/lineitems?provider=Azure&invoiceLineItemType=DailyRatedUsageLineItems&currencyCode=USD&period=current&size=2000";

                    var nceResponse = await client.GetAsync(nceUrl);
                    if (!nceResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[ZENITH] Error Global AP: {nceResponse.StatusCode} - {await nceResponse.Content.ReadAsStringAsync()}");
                        continue;
                    }

                    var content = await nceResponse.Content.ReadAsStringAsync();
                    var pcData = JsonDocument.Parse(content);

                    if (!pcData.RootElement.TryGetProperty("items", out var items)) continue;

                    int insertedCount = 0;
                    foreach (var item in items.EnumerateArray())
                    {
                        // 3. Filtrado y Mapeo
                        var itemSubIdStr = item.TryGetProperty("subscriptionId", out var sid) && sid.ValueKind != JsonValueKind.Null ? sid.GetString() : null;
                        if (string.IsNullOrEmpty(itemSubIdStr) || !Guid.TryParse(itemSubIdStr, out var subId) || !nceSubs.TryGetValue(subId, out var sub))
                            continue;

                        decimal quantity = item.TryGetProperty("billedQuantity", out var q) && q.ValueKind != JsonValueKind.Null ? q.GetDecimal() : 0m;
                        decimal rawCost = item.TryGetProperty("pretaxCharges", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetDecimal() : 0m;
                        if (rawCost == 0m && quantity == 0m) continue;

                        var resourceName = item.TryGetProperty("meterName", out var mn) && mn.ValueKind != JsonValueKind.Null ? mn.GetString() : "N/A";

                        // Extracción de Tags FinOps
                        string rawTags = item.TryGetProperty("tags", out var tg) && tg.ValueKind != JsonValueKind.Null ? tg.GetString() : "{}";
                        string envTag = ExtractTagValue(rawTags, "Environment");
                        string costCenterTag = ExtractTagValue(rawTags, "CostCenter");

                        decimal calculatedBilledCost = rawCost * (1 + sub.Markup);

                        // 4. Inserción Defensiva (Evitar duplicados diarios)
                        bool exists = await _context.PCUsageRecords.AnyAsync(u =>
                            u.SubscriptionId == sub.Id && u.UsageDate == targetDate && u.ResourceName == resourceName);

                        if (!exists)
                        {
                            _context.PCUsageRecords.Add(new PCUsageRecord
                            {
                                TenantId = sub.TenantId,  // Vital para el rendimiento
                                SubscriptionId = sub.Id,
                                UsageDate = targetDate,
                                ProductName = item.TryGetProperty("productName", out var pn) && pn.ValueKind != JsonValueKind.Null ? pn.GetString() : "N/A",
                                MeterCategory = item.TryGetProperty("meterCategory", out var mc) && mc.ValueKind != JsonValueKind.Null ? mc.GetString() : "N/A",
                                Quantity = quantity,
                                ResourceId = item.TryGetProperty("meterId", out var ri) && ri.ValueKind != JsonValueKind.Null ? ri.GetString() : "N/A",
                                ResourceName = resourceName,
                                EstimatedCost = rawCost,
                                MarkupPercentage = sub.Markup,
                                BilledCost = calculatedBilledCost,
                                Currency = "USD",
                                ProviderSource = "PartnerCenter_NCE_AP",
                                ChargeType = "Usage",
                                TagsJson = rawTags,
                                FinOpsEnvironment = envTag,
                                FinOpsCostCenter = costCenterTag
                            });
                            insertedCount++;
                        }
                    }
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"[ZENITH] Insertados {insertedCount} registros NCE para {config.CountryName}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR NIGHTLY PC NCE] {config.CountryName}: {ex.Message}");
                }
            }
        }

        private string ExtractTagValue(string jsonTags, string key)
        {
            if (string.IsNullOrWhiteSpace(jsonTags) || jsonTags == "{}") return null;
            try
            {
                var jDoc = JsonDocument.Parse(jsonTags);
                return jDoc.RootElement.TryGetProperty(key, out var val) ? val.GetString() : null;
            }
            catch { return null; }
        }

        // --- CONFIGURACIÓN HTTP ---
        private async Task<AuthenticationResult> GetTokenAsync(PartnerCenterCredential config)
        {
            var plainTextSecret = _protector.Unprotect(config.ClientSecret);
            var app = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                .WithClientSecret(plainTextSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{config.TenantId}"))
                .Build();

            string[] scopes = new string[] { "https://api.partnercenter.microsoft.com/.default" };
            return await app.AcquireTokenForClient(scopes).ExecuteAsync();
        }

        private HttpClient CreateHttpClient(string token)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }
    }
}