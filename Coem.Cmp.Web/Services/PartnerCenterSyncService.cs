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
        // --- NUEVO: Motor nocturno para CSP ---
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

        public async Task<int> SyncCustomersAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();

            if (!regionalConfigs.Any())
            {
                throw new InvalidOperationException("Bóveda Regional vacía. Configura un país antes de sincronizar.");
            }

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
                            var companyName = item.GetProperty("companyProfile").GetProperty("companyName").GetString();
                            var domain = item.GetProperty("companyProfile").GetProperty("domain").GetString();

                            var existingTenant = await _context.Tenants.FirstOrDefaultAsync(t => t.MicrosoftTenantId == pcTenantId);

                            if (existingTenant == null)
                            {
                                _context.Tenants.Add(new Tenant
                                {
                                    MicrosoftTenantId = pcTenantId,
                                    Name = companyName,
                                    DefaultDomain = domain,
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
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR REGIONAL] {config.CountryName}: {ex.Message}");
                }
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

                    var tenants = await _context.Tenants
                                        .Where(t => t.Country == config.CountryName)
                                        .ToListAsync();

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
                            var status = item.GetProperty("status").GetString();

                            DateTime? effectiveDate = null;
                            if (item.TryGetProperty("effectiveStartDate", out var dateElement) && dateElement.ValueKind != JsonValueKind.Null)
                            {
                                effectiveDate = dateElement.GetDateTime();
                            }

                            string categoryTag = "Colab";
                            if (offerName.Contains("Azure plan", StringComparison.OrdinalIgnoreCase) || offerId.Contains("DZH318Z0BPS6"))
                            {
                                categoryTag = "AP";
                            }
                            else if (offerName.Contains("Sponsorship", StringComparison.OrdinalIgnoreCase) || offerName.Contains("Pass", StringComparison.OrdinalIgnoreCase))
                            {
                                categoryTag = "INT";
                            }
                            else if (offerName.Contains("Microsoft Azure", StringComparison.OrdinalIgnoreCase) || offerId.Contains("MS-AZR-0145P"))
                            {
                                categoryTag = "AL";
                            }

                            var existingSub = await _context.Subscriptions.FirstOrDefaultAsync(s => s.Id == subId);

                            if (existingSub != null)
                            {
                                existingSub.Status = status;
                                existingSub.EffectiveDate = effectiveDate;
                                existingSub.OfferName = offerName;
                            }
                            else
                            {
                                _context.Subscriptions.Add(new Subscription
                                {
                                    Id = subId,
                                    TenantId = tenant.Id,
                                    OfferId = offerId,
                                    OfferName = offerName,
                                    Category = categoryTag,
                                    CreatedDate = item.GetProperty("creationDate").GetDateTime(),
                                    EffectiveDate = effectiveDate,
                                    Status = status,
                                    Markup = 0.00m // Por defecto 0% en CSP hasta ser ajustado
                                });
                            }
                            processedCount++;
                        }
                        await _context.SaveChangesAsync();
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR SYNC] {config.CountryName}: {ex.Message}");
                }
            }
            return processedCount;
        }

        // --- MOTOR NOCTURNO PARA CSP (EL CEREBRO FINANCIERO ZENITH) ---
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

                    // Traemos solo los tenants de esta credencial
                    var tenants = await _context.Tenants.Where(t => t.Country == config.CountryName).ToListAsync();

                    foreach (var tenant in tenants)
                    {
                        // Solo buscamos uso de Azure Plan y Azure Legacy
                        var subscriptions = await _context.Subscriptions
                            .Where(s => s.TenantId == tenant.Id && (s.Category == "AP" || s.Category == "AL"))
                            .ToListAsync();

                        foreach (var sub in subscriptions)
                        {
                            if (sub.Category == "AL")
                            {
                                // FLUJO 1: AZURE LEGACY
                                string url = $"https://api.partnercenter.microsoft.com/v1/customers/{tenant.MicrosoftTenantId}/subscriptions/{sub.Id}/utilizations?start_time={targetDate:yyyy-MM-dd}T00:00:00Z&end_time={targetDate:yyyy-MM-dd}T23:59:59Z";
                                var response = await client.GetAsync(url);

                                if (!response.IsSuccessStatusCode)
                                {
                                    Console.WriteLine($"[ZENITH] Ignorando AL sub {sub.Id} - Status: {response.StatusCode}");
                                    continue;
                                }

                                var content = await response.Content.ReadAsStringAsync();
                                var usageData = JsonDocument.Parse(content);

                                if (!usageData.RootElement.TryGetProperty("items", out var items)) continue;

                                foreach (var item in items.EnumerateArray())
                                {
                                    var resource = item.GetProperty("resource");
                                    var resourceName = resource.GetProperty("name").GetString();
                                    var quantity = item.GetProperty("quantity").GetDecimal();

                                    decimal rawCost = 1.0m; // MVP Placeholder Legacy (Cambiar por cruce con Rate Card en el futuro)
                                    decimal calculatedBilledCost = rawCost * (1 + sub.Markup);

                                    bool exists = await _context.PCUsageRecords.AnyAsync(u =>
                                        u.SubscriptionId == sub.Id && u.UsageDate == targetDate && u.ResourceName == resourceName);

                                    if (!exists)
                                    {
                                        _context.PCUsageRecords.Add(new PCUsageRecord
                                        {
                                            SubscriptionId = sub.Id,
                                            UsageDate = targetDate,
                                            ProductName = resource.GetProperty("subcategory").GetString(),
                                            MeterCategory = resource.GetProperty("category").GetString(),
                                            Quantity = quantity,
                                            ResourceId = resource.GetProperty("id").GetString(),
                                            ResourceName = resourceName,
                                            EstimatedCost = rawCost,
                                            MarkupPercentage = sub.Markup,
                                            BilledCost = calculatedBilledCost,
                                            Currency = "USD",
                                            ProviderSource = "PartnerCenter_AL"
                                        });
                                    }
                                }
                            }
                            else if (sub.Category == "AP")
                            {
                                // FLUJO 2: AZURE PLAN (NCE) - El estándar de oro
                                string url = $"https://api.partnercenter.microsoft.com/v1/customers/{tenant.MicrosoftTenantId}/invoices/unbilled/lineitems?provider=azure&invoicelineitemtype=billinglineitems";
                                var response = await client.GetAsync(url);

                                if (!response.IsSuccessStatusCode)
                                {
                                    Console.WriteLine($"[ZENITH] Error AP en Tenant {tenant.MicrosoftTenantId} - Status: {response.StatusCode}");
                                    continue;
                                }

                                var content = await response.Content.ReadAsStringAsync();
                                var pcData = JsonDocument.Parse(content);

                                if (!pcData.RootElement.TryGetProperty("items", out var items)) continue;

                                foreach (var item in items.EnumerateArray())
                                {
                                    // 1. Filtro de suscripción defensivo
                                    var itemSubIdStr = item.TryGetProperty("subscriptionId", out var sid) && sid.ValueKind != JsonValueKind.Null ? sid.GetString() : null;
                                    if (string.IsNullOrEmpty(itemSubIdStr) || !Guid.TryParse(itemSubIdStr, out var itemSubId) || itemSubId != sub.Id) continue;

                                    // 2. Extracción Defensiva Zenith (Blindaje contra nulos)
                                    var resourceName = item.TryGetProperty("meterName", out var mn) && mn.ValueKind != JsonValueKind.Null ? mn.GetString() : "Recurso Desconocido";
                                    var productName = item.TryGetProperty("productName", out var pn) && pn.ValueKind != JsonValueKind.Null ? pn.GetString() : "Producto N/A";
                                    var meterCategory = item.TryGetProperty("meterCategory", out var mc) && mc.ValueKind != JsonValueKind.Null ? mc.GetString() : "Categoria N/A";
                                    var resourceId = item.TryGetProperty("resourceId", out var ri) && ri.ValueKind != JsonValueKind.Null ? ri.GetString() : "ID_No_Provisto";
                                    var currency = item.TryGetProperty("currency", out var curr) && curr.ValueKind != JsonValueKind.Null ? curr.GetString() : "USD";

                                    decimal quantity = item.TryGetProperty("consumedQuantity", out var q) && q.ValueKind != JsonValueKind.Null ? q.GetDecimal() : 0m;
                                    decimal rawCost = item.TryGetProperty("pretaxCharges", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetDecimal() : 0m;

                                    // 3. Regla de Negocio: Si no hay cantidad ni costo, es ruido de la API. Lo saltamos.
                                    if (rawCost == 0m && quantity == 0m) continue;

                                    decimal calculatedBilledCost = rawCost * (1 + sub.Markup);

                                    // 4. Verificación de duplicados e Inserción
                                    bool exists = await _context.PCUsageRecords.AnyAsync(u =>
                                        u.SubscriptionId == sub.Id && u.UsageDate == targetDate && u.ResourceName == resourceName);

                                    if (!exists)
                                    {
                                        _context.PCUsageRecords.Add(new PCUsageRecord
                                        {
                                            SubscriptionId = sub.Id,
                                            UsageDate = targetDate,
                                            ProductName = productName,
                                            MeterCategory = meterCategory,
                                            Quantity = quantity,
                                            ResourceId = resourceId,
                                            ResourceName = resourceName,
                                            EstimatedCost = rawCost,
                                            MarkupPercentage = sub.Markup,
                                            BilledCost = calculatedBilledCost,
                                            Currency = currency,
                                            ProviderSource = "PartnerCenter_AP"
                                        });
                                    }
                                }
                            }
                        }
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR NIGHTLY PC] {config.CountryName}: {ex.Message}");
                }
            }
        }

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