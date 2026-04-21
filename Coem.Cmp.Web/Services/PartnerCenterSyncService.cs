using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Coem.Cmp.Web.Services
{
    public interface IPartnerCenterSyncService
    {
        Task<int> SyncCustomersAsync();
        Task<int> SyncSubscriptionsAsync();
        Task SyncNightlyUsageAsync(string? billingPeriod = "current");
        Task SyncCspOperationalConsumptionAsync();
        Task<int> SyncSaaSSubscriptionsAsync();
    }

    public class PartnerCenterSyncService : IPartnerCenterSyncService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDataProtector _protector;
        private readonly ILogger<PartnerCenterSyncService> _logger;

        public PartnerCenterSyncService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<PartnerCenterSyncService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _protector = dataProtectionProvider.CreateProtector("Coem.Cmp.RegionalSecrets.v1");
            _logger = logger;
        }

        // --- 1. SINCRONIZACIÓN DE CLIENTES (TENANTS) ---
        public async Task<int> SyncCustomersAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            if (!regionalConfigs.Any()) throw new InvalidOperationException("Bóveda Regional vacía.");

            int totalSynced = 0;
            foreach (var config in regionalConfigs)
            {
                try
                {
                    var app = BuildMsalApp(config);
                    var authResult = await app.AcquireTokenForClient(new[] { "https://api.partnercenter.microsoft.com/.default" }).ExecuteAsync();
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
                            var tenantIdStr = item.GetProperty("companyProfile").GetProperty("tenantId").GetString();
                            if (string.IsNullOrEmpty(tenantIdStr)) continue;

                            var pcTenantId = Guid.Parse(tenantIdStr);
                            var existingTenant = await _context.Tenants.FirstOrDefaultAsync(t => t.MicrosoftTenantId == pcTenantId);

                            if (existingTenant == null)
                            {
                                _context.Tenants.Add(new Tenant
                                {
                                    MicrosoftTenantId = pcTenantId,
                                    Name = item.GetProperty("companyProfile").GetProperty("companyName").GetString() ?? "Unknown",
                                    DefaultDomain = item.GetProperty("companyProfile").GetProperty("domain").GetString() ?? "unknown.onmicrosoft.com",
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

                        currentUrl = null!;
                        if (pcData.RootElement.TryGetProperty("links", out var links) && links.TryGetProperty("next", out var nextLink))
                        {
                            var uriString = nextLink.GetProperty("uri").GetString();
                            currentUrl = uriString?.StartsWith("http") == true ? uriString : $"https://api.partnercenter.microsoft.com/{(uriString?.StartsWith("/") == true ? uriString.Substring(1) : uriString)}";
                        }
                    } while (!string.IsNullOrEmpty(currentUrl));
                }
                catch (Exception ex) { _logger.LogError($"[ERROR REGIONAL CUSTOMERS] {config.CountryName}: {ex.Message}"); }
            }
            return totalSynced;
        }

        // --- 2. MODULO SaaS: LICENCIAS ---
        public async Task<int> SyncSaaSSubscriptionsAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            var categoryRules = await _context.CategoryMappings.Where(c => c.IsActive).OrderBy(c => c.Priority).AsNoTracking().ToListAsync();
            int totalSaaSProcessed = 0;

            foreach (var config in regionalConfigs)
            {
                try
                {
                    var app = BuildMsalApp(config);
                    var client = _httpClientFactory.CreateClient();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var tenants = await _context.Tenants.Where(t => t.Country == config.CountryName).ToListAsync();

                    foreach (var tenant in tenants)
                    {
                        var authResult = await app.AcquireTokenForClient(new[] { "https://api.partnercenter.microsoft.com/.default" }).ExecuteAsync();
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);

                        string url = $"https://api.partnercenter.microsoft.com/v1/customers/{tenant.MicrosoftTenantId}/subscriptions";
                        var response = await client.GetAsync(url);
                        if (!response.IsSuccessStatusCode) continue;

                        var pcData = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        if (!pcData.RootElement.TryGetProperty("items", out var items)) continue;

                        foreach (var item in items.EnumerateArray())
                        {
                            var billingType = item.TryGetProperty("billingType", out var bt) ? bt.GetString() : "";
                            if (billingType != "license") continue;

                            var offerName = item.TryGetProperty("offerName", out var on) ? on.GetString() ?? "" : "";
                            var offerId = item.TryGetProperty("offerId", out var oi) ? oi.GetString() ?? "" : "";
                            var msSubId = Guid.Parse(item.GetProperty("id").GetString()!);
                            int quantity = item.TryGetProperty("quantity", out var q) ? q.GetInt32() : 0;
                            string status = item.TryGetProperty("status", out var s) ? s.GetString() ?? "Unknown" : "Unknown";

                            string categoryTag = "SaaS";
                            foreach (var rule in categoryRules)
                            {
                                if (offerName.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase) ||
                                    offerId.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase))
                                {
                                    categoryTag = rule.CategoryCode;
                                    break;
                                }
                            }

                            var existingSub = await _context.Subscriptions
                                .FirstOrDefaultAsync(sub => sub.MicrosoftSubscriptionId == msSubId);

                            if (existingSub != null)
                            {
                                existingSub.Status = status;
                                existingSub.Name = offerName;
                                existingSub.Quantity = quantity;
                                existingSub.Category = categoryTag;
                            }
                            else
                            {
                                _context.Subscriptions.Add(new Subscription
                                {
                                    MicrosoftSubscriptionId = msSubId,
                                    TenantId = tenant.Id,
                                    OfferId = offerId,
                                    Name = offerName,
                                    Category = categoryTag,
                                    CreatedDate = DateTime.UtcNow,
                                    Status = status,
                                    Quantity = quantity,
                                    IsAzureWorkload = false,
                                    Markup = 0.00m
                                });
                            }
                            totalSaaSProcessed++;
                        }
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex) { _logger.LogError($"[ERROR SaaS] {config.CountryName}: {ex.Message}"); }
            }
            return totalSaaSProcessed;
        }

        // --- 3. SINCRONIZACIÓN DE SUSCRIPCIONES (Azure Workloads) ---
        public async Task<int> SyncSubscriptionsAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            var categoryRules = await _context.CategoryMappings.Where(c => c.IsActive).OrderBy(c => c.Priority).AsNoTracking().ToListAsync();
            int processedCount = 0;

            foreach (var config in regionalConfigs)
            {
                try
                {
                    var app = BuildMsalApp(config);
                    var client = CreateHttpClient((await app.AcquireTokenForClient(new[] { "https://api.partnercenter.microsoft.com/.default" }).ExecuteAsync()).AccessToken);
                    var tenants = await _context.Tenants.Where(t => t.Country == config.CountryName).ToListAsync();

                    foreach (var tenant in tenants)
                    {
                        var authResult = await app.AcquireTokenForClient(new[] { "https://api.partnercenter.microsoft.com/.default" }).ExecuteAsync();
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);

                        var response = await client.GetAsync($"https://api.partnercenter.microsoft.com/v1/customers/{tenant.MicrosoftTenantId}/subscriptions");
                        if (!response.IsSuccessStatusCode) continue;

                        var pcData = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        if (!pcData.RootElement.TryGetProperty("items", out var items)) continue;

                        foreach (var item in items.EnumerateArray())
                        {
                            var offerName = item.TryGetProperty("offerName", out var on) ? on.GetString() ?? "Unknown" : "Unknown";
                            if (!offerName.Contains("Azure Plan", StringComparison.OrdinalIgnoreCase)) continue;

                            var msSubId = Guid.Parse(item.GetProperty("id").GetString()!);
                            string categoryTag = "AL";
                            foreach (var rule in categoryRules)
                            {
                                if (offerName.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase)) { categoryTag = rule.CategoryCode; break; }
                            }

                            var existingSub = await _context.Subscriptions
                                .FirstOrDefaultAsync(s => s.MicrosoftSubscriptionId == msSubId);

                            string status = item.TryGetProperty("status", out var st) ? st.GetString() ?? "Active" : "Active";

                            if (existingSub != null)
                            {
                                existingSub.Status = status;
                                existingSub.Name = offerName;
                                existingSub.Category = categoryTag;
                            }
                            else
                            {
                                _context.Subscriptions.Add(new Subscription
                                {
                                    MicrosoftSubscriptionId = msSubId,
                                    TenantId = tenant.Id,
                                    OfferId = item.TryGetProperty("offerId", out var oi) ? oi.GetString() ?? "Unknown" : "Unknown",
                                    Name = offerName,
                                    Category = categoryTag,
                                    CreatedDate = DateTime.UtcNow,
                                    Status = status,
                                    IsAzureWorkload = true,
                                    Markup = 0.00m
                                });
                            }
                            processedCount++;
                        }
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex) { _logger.LogError($"[ERROR SYNC SUBS] {config.CountryName}: {ex.Message}"); }
            }
            return processedCount;
        }

        // --- 4. MOTOR NCE FINANCIERO (Graph API v2) ---
        public async Task SyncNightlyUsageAsync(string? billingPeriod = "current")
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            foreach (var config in regionalConfigs)
            {
                try
                {
                    var app = BuildMsalApp(config);
                    var authResult = await app.AcquireTokenForClient(new[] { "https://graph.microsoft.com/.default" }).ExecuteAsync();
                    var client = CreateHttpClient(authResult.AccessToken);

                    string exportType = billingPeriod == "current" ? "unbilled" : "billed";
                    var response = await client.PostAsJsonAsync($"https://graph.microsoft.com/v1.0/reports/partners/billing/usage/{exportType}/export", new { billingPeriod, currencyCode = "USD", attributeSet = "full" });

                    if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                    {
                        var operationUrl = response.Headers.Location?.ToString();
                        if (operationUrl == null) continue;

                        int attempts = 0;
                        while (attempts < 30)
                        {
                            attempts++; await Task.Delay(30000);
                            var pollAuth = await app.AcquireTokenForClient(new[] { "https://graph.microsoft.com/.default" }).ExecuteAsync();
                            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pollAuth.AccessToken);

                            var statusResponse = await client.GetAsync(operationUrl);
                            if (!statusResponse.IsSuccessStatusCode) continue;

                            var root = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync()).RootElement;
                            string status = root.TryGetProperty("status", out var s) ? s.GetString()?.ToLowerInvariant() ?? "" : "";
                            if (status == "completed" || status == "succeeded") { await ProcessManifestFiles(root.Clone(), config, billingPeriod!); break; }
                            if (status == "failed") break;
                        }
                    }
                }
                catch (Exception ex) { _logger.LogError($"[FATAL NCE] {config.CountryName}: {ex.Message}"); }
            }
        }

        // --- 5. RADAR CSP OPERATIVO (BLINDADO Y RESILIENTE) ---
        public async Task SyncCspOperationalConsumptionAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            foreach (var config in regionalConfigs)
            {
                try
                {
                    var app = BuildMsalApp(config);
                    var client = CreateHttpClient((await app.AcquireTokenForClient(new[] { "https://management.azure.com/.default" }).ExecuteAsync()).AccessToken);

                    var baResponse = await client.GetAsync("https://management.azure.com/providers/Microsoft.Billing/billingAccounts?api-version=2020-05-01");
                    if (!baResponse.IsSuccessStatusCode) continue;

                    var baDoc = JsonDocument.Parse(await baResponse.Content.ReadAsStringAsync());
                    if (!baDoc.RootElement.TryGetProperty("value", out var baArray) || baArray.GetArrayLength() == 0) continue;

                    string billingAccountId = baArray[0].TryGetProperty("name", out var bn) ? bn.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(billingAccountId)) continue;

                    var tenants = await _context.Tenants.Where(t => t.Country == config.CountryName).ToListAsync();

                    foreach (var tenant in tenants)
                    {
                        var loopAuth = await app.AcquireTokenForClient(new[] { "https://management.azure.com/.default" }).ExecuteAsync();
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loopAuth.AccessToken);

                        await _context.PCUsageRecords.Where(r => r.TenantId == tenant.Id && r.UsageDate >= startOfMonth && r.ProviderSource == "CostManagement_Operational").ExecuteDeleteAsync();

                        string requestUrl = $"https://management.azure.com/providers/Microsoft.Billing/billingAccounts/{billingAccountId}/customers/{tenant.MicrosoftTenantId}/providers/Microsoft.Consumption/usageDetails?metric=AmortizedCost&$filter=properties/usageStart ge '{startOfMonth:yyyy-MM-dd}'&api-version=2023-11-01";
                        HttpResponseMessage usageResponse = null;
                        int maxRetries = 3;

                        // ESTRATEGIA EXPONENTIAL BACKOFF
                        for (int attempt = 1; attempt <= maxRetries; attempt++)
                        {
                            usageResponse = await client.GetAsync(requestUrl);

                            if (usageResponse.IsSuccessStatusCode) break;

                            if ((int)usageResponse.StatusCode == 429)
                            {
                                var delay = usageResponse.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt) * 5);
                                _logger.LogWarning($"[THROTTLING 429] Límite de API para Tenant {tenant.MicrosoftTenantId}. Pausando {delay.TotalSeconds}s (Intento {attempt}/{maxRetries}).");
                                await Task.Delay(delay);
                                continue;
                            }

                            break;
                        }

                        if (usageResponse == null || !usageResponse.IsSuccessStatusCode)
                        {
                            _logger.LogWarning($"[SKIP] Omitiendo consumo para Tenant {tenant.MicrosoftTenantId} tras fallos o sin permisos.");
                            continue;
                        }

                        var responseContent = await usageResponse.Content.ReadAsStringAsync();
                        var parsedDoc = JsonDocument.Parse(responseContent);

                        if (!parsedDoc.RootElement.TryGetProperty("value", out var records)) continue;

                        var batch = new List<PCUsageRecord>();

                        foreach (var record in records.EnumerateArray())
                        {
                            if (!record.TryGetProperty("properties", out var props)) continue;

                            // 1. Extracción táctica
                            var subGuidStr = props.TryGetProperty("subscriptionGuid", out var sg) ? sg.GetString() :
                                             (props.TryGetProperty("subscriptionId", out var si) ? si.GetString() : null);

                            // 2. Limpieza de basura de Azure ARM
                            if (!string.IsNullOrEmpty(subGuidStr) && subGuidStr.Contains("/subscriptions/"))
                            {
                                subGuidStr = subGuidStr.Replace("/subscriptions/", "").Split('/')[0];
                            }

                            // 3. Parseo y Radar de diagnóstico
                            if (!Guid.TryParse(subGuidStr, out Guid currentSubId))
                            {
                                string availableKeys = string.Join(", ", props.EnumerateObject().Select(p => p.Name));
                                _logger.LogWarning($"[ALERTA DE ESQUEMA] No se pudo extraer el GUID. Valor recibido: '{subGuidStr ?? "NULL"}'. Nodos disponibles: {availableKeys}");
                            }

                            batch.Add(new PCUsageRecord
                            {
                                TenantId = tenant.Id,
                                SubscriptionId = currentSubId, // Asignación limpia a la base
                                UsageDate = props.TryGetProperty("usageStart", out var us) && us.ValueKind == JsonValueKind.String ? DateTime.Parse(us.GetString()!) : startOfMonth,
                                ProductName = props.TryGetProperty("product", out var p) ? p.GetString() ?? "Unknown" : "Unknown",
                                MeterCategory = props.TryGetProperty("meterCategory", out var m) ? m.GetString() ?? "N/A" : "N/A",
                                Quantity = props.TryGetProperty("quantity", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetDecimal() : 0m,
                                EstimatedCost = props.TryGetProperty("costInBillingCurrency", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetDecimal() : 0m,
                                BilledCost = props.TryGetProperty("costInBillingCurrency", out var bc) && bc.ValueKind == JsonValueKind.Number ? bc.GetDecimal() : 0m,
                                Currency = props.TryGetProperty("billingCurrencyCode", out var cur) ? cur.GetString() ?? "USD" : "USD",
                                ProviderSource = "CostManagement_Operational"
                            });

                            if (batch.Count >= 1000)
                            {
                                _context.PCUsageRecords.AddRange(batch);
                                await _context.SaveChangesAsync();
                                batch.Clear();
                            }
                        }
                        if (batch.Any())
                        {
                            _context.PCUsageRecords.AddRange(batch);
                            await _context.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex) { _logger.LogError($"[ERROR RADAR CSP] {config.CountryName}: {ex.Message}"); }
            }
        }

        private async Task ProcessManifestFiles(JsonElement statusData, PartnerCenterCredential config, string billingPeriod)
        {
            await Task.CompletedTask;
        }

        private IConfidentialClientApplication BuildMsalApp(PartnerCenterCredential config)
        {
            var plainTextSecret = _protector.Unprotect(config.ClientSecret);
            return ConfidentialClientApplicationBuilder.Create(config.ClientId)
                .WithClientSecret(plainTextSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{config.TenantId}"))
                .Build();
        }

        private HttpClient CreateHttpClient(string token)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
    }
}