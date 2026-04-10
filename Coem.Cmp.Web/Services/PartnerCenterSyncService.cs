using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json; // VITAL: Para PostAsJsonAsync y ReadFromJsonAsync
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging; // VITAL: Para el sistema de Logs

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
        private readonly ILogger<PartnerCenterSyncService> _logger; // Declarado correctamente

        public PartnerCenterSyncService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<PartnerCenterSyncService> logger) // Inyectado en el constructor
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _protector = dataProtectionProvider.CreateProtector("Coem.Cmp.RegionalSecrets.v1");
            _logger = logger;
        }

        // --- SINCRONIZACIÓN DE CLIENTES (Partner Center API) ---
        public async Task<int> SyncCustomersAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            if (!regionalConfigs.Any()) throw new InvalidOperationException("Bóveda Regional vacía.");

            int totalSynced = 0;
            foreach (var config in regionalConfigs)
            {
                try
                {
                    var authResult = await GetTokenAsync(config, isGraph: false); // Llama a la firma corregida
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
                catch (Exception ex) { _logger.LogError($"[ERROR REGIONAL CUSTOMERS] {config.CountryName}: {ex.Message}"); }
            }
            return totalSynced;
        }

        // --- SINCRONIZACIÓN DE SUSCRIPCIONES (Partner Center API) ---
        public async Task<int> SyncSubscriptionsAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            int processedCount = 0;

            foreach (var config in regionalConfigs)
            {
                try
                {
                    var authResult = await GetTokenAsync(config, isGraph: false);
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

                            string categoryTag = (offerName.Contains("Azure plan", StringComparison.OrdinalIgnoreCase) || offerId.Contains("DZH318Z0BPS6")) ? "AP" : "AL";

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
                catch (Exception ex) { _logger.LogError($"[ERROR SYNC SUBS] {config.CountryName}: {ex.Message}"); }
            }
            return processedCount;
        }

        // --- MOTOR NCE ASÍNCRONO (Microsoft Graph Billing API) ---
        public async Task SyncNightlyUsageAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();

            foreach (var config in regionalConfigs)
            {
                try
                {
                    _logger.LogInformation($"[ZENITH] Solicitando exportación NCE vía Graph para {config.CountryName}...");

                    var authResult = await GetTokenAsync(config, isGraph: true); // Firma corregida
                    var client = CreateHttpClient(authResult.AccessToken);

                    var requestBody = new { billingPeriod = "current", currencyCode = "USD", attributeSet = "full" };
                    var response = await client.PostAsJsonAsync("https://graph.microsoft.com/v1.0/reports/partners/billing/usage/unbilled/export", requestBody); // Usando extensiones de JSON

                    if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                    {
                        string operationUrl = response.Headers.Location.ToString();
                        bool isCompleted = false;

                        while (!isCompleted)
                        {
                            _logger.LogInformation($"[ZENITH] Esperando reporte de {config.CountryName}...");
                            await Task.Delay(30000);

                            var statusResponse = await client.GetAsync(operationUrl);
                            var statusData = await statusResponse.Content.ReadFromJsonAsync<JsonElement>(); // Firma corregida

                            string status = statusData.GetProperty("status").GetString();
                            if (status == "completed")
                            {
                                await ProcessManifestFiles(statusData, config);
                                isCompleted = true;
                            }
                            else if (status == "failed") break;
                        }
                    }
                }
                catch (Exception ex) { _logger.LogError($"[ZENITH FATAL] Error en {config.CountryName}: {ex.Message}"); }
            }
        }

        private async Task ProcessManifestFiles(JsonElement statusData, PartnerCenterCredential config)
        {
            _logger.LogInformation($"[ZENITH] Procesando manifiesto de descarga para {config.CountryName}...");
        }

        // --- AYUDANTES CON FIRMAS CORREGIDAS ---
        private async Task<AuthenticationResult> GetTokenAsync(PartnerCenterCredential config, bool isGraph) // AHORA ACEPTA isGraph
        {
            var plainTextSecret = _protector.Unprotect(config.ClientSecret);
            var app = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                .WithClientSecret(plainTextSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{config.TenantId}"))
                .Build();

            string scope = isGraph ? "https://graph.microsoft.com/.default" : "https://api.partnercenter.microsoft.com/.default";
            return await app.AcquireTokenForClient(new[] { scope }).ExecuteAsync();
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