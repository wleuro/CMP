using System.Net.Http.Headers;
using System.Text.Json;
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

                    // Traemos todos los tenants de este país
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
                            var effectiveDate = item.GetProperty("effectiveStartDate").GetDateTime();

                            // Lógica de Categorización Zenith
                            string categoryTag = "Colab";
                            if (offerName.Contains("Azure plan", StringComparison.OrdinalIgnoreCase) || offerId.Contains("DZH318Z0BPS6"))
                                categoryTag = "AP";
                            else if (offerName.Contains("Microsoft Azure", StringComparison.OrdinalIgnoreCase) || offerId.Contains("MS-AZR-0145P"))
                                categoryTag = "AL";

                            // BUSCAMOS SI YA EXISTE (POR GUID) PARA ACTUALIZARLO
                            var existingSub = await _context.Subscriptions.FirstOrDefaultAsync(s => s.Id == subId);

                            if (existingSub != null)
                            {
                                // ACTUALIZACIÓN: Si ya existe, refrescamos el estado y la fecha de cambio
                                existingSub.Status = status;
                                existingSub.EffectiveDate = effectiveDate;
                                existingSub.OfferName = offerName; // Por si cambió el SKU
                            }
                            else
                            {
                                // INSERCIÓN: Nueva suscripción detectada
                                _context.Subscriptions.Add(new Subscription
                                {
                                    Id = subId,
                                    TenantId = tenant.Id,
                                    OfferId = offerId,
                                    OfferName = offerName,
                                    Category = categoryTag,
                                    CreatedDate = item.GetProperty("creationDate").GetDateTime(),
                                    EffectiveDate = effectiveDate,
                                    Status = status
                                });
                            }
                            processedCount++;
                        }
                        // Guardamos por cada Tenant para no saturar la memoria
                        await _context.SaveChangesAsync();
                        await Task.Delay(100); // Cortesía con la API de MS
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR SYNC] {config.CountryName}: {ex.Message}");
                }
            }
            return processedCount;
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