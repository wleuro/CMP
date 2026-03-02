using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;

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

        public PartnerCenterSyncService(ApplicationDbContext context, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<int> SyncCustomersAsync()
        {
            // 1. Obtenemos todas las credenciales regionales activas (Panel de Control)
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            int totalSynced = 0;

            foreach (var config in regionalConfigs)
            {
                Console.WriteLine($"[REGION] Iniciando carga de clientes para: {config.CountryName}");

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
                                    Country = config.CountryName, // TAG REGIONAL
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
                    Console.WriteLine($"[ERROR REGIONAL] Fallo en {config.CountryName}: {ex.Message}");
                }
            }
            return totalSynced;
        }

        public async Task<int> SyncSubscriptionsAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            int totalSubscriptions = 0;

            foreach (var config in regionalConfigs)
            {
                Console.WriteLine($"[REGION] Auditando suscripciones de: {config.CountryName}");

                var authResult = await GetTokenAsync(config);
                var client = CreateHttpClient(authResult.AccessToken);

                // Traemos solo los clientes que pertenecen a este país
                var tenants = await _context.Tenants
                                    .Where(t => t.AgreementType == "CSP" && t.Country == config.CountryName)
                                    .ToListAsync();

                foreach (var tenant in tenants)
                {
                    bool yaAuditado = await _context.Subscriptions.AnyAsync(s => s.TenantId == tenant.Id);
                    if (yaAuditado) continue;

                    string url = $"https://api.partnercenter.microsoft.com/v1/customers/{tenant.MicrosoftTenantId}/subscriptions";

                    try
                    {
                        var response = await client.GetAsync(url);
                        if (!response.IsSuccessStatusCode)
                        {
                            var errorMsg = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"[AVISO] {tenant.Name} ({config.CountryName}): {response.StatusCode}");
                            continue;
                        }

                        var content = await response.Content.ReadAsStringAsync();
                        var pcData = JsonDocument.Parse(content);

                        if (!pcData.RootElement.TryGetProperty("items", out var items)) continue;

                        foreach (var item in items.EnumerateArray())
                        {
                            var subId = Guid.Parse(item.GetProperty("id").GetString());
                            var offerName = item.GetProperty("offerName").GetString();
                            var offerId = item.GetProperty("offerId").GetString();

                            string categoryTag = "Colab";
                            if (offerName.Contains("Azure plan", StringComparison.OrdinalIgnoreCase) || offerId.Contains("DZH318Z0BPS6"))
                                categoryTag = "AP";
                            else if (offerName.Contains("Microsoft Azure", StringComparison.OrdinalIgnoreCase) || offerId.Contains("MS-AZR-0145P"))
                                categoryTag = "AL";

                            _context.Subscriptions.Add(new Subscription
                            {
                                Id = subId,
                                TenantId = tenant.Id,
                                OfferId = offerId,
                                OfferName = offerName,
                                Category = categoryTag,
                                CreatedDate = item.GetProperty("creationDate").GetDateTime(),
                                Status = item.GetProperty("status").GetString()
                            });
                            totalSubscriptions++;
                        }
                        await _context.SaveChangesAsync();
                        await Task.Delay(200);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR CRITICO] {tenant.Name}: {ex.Message}");
                    }
                }
            }
            return totalSubscriptions;
        }

        // MÉTODOS PRIVADOS DE APOYO (DRY)
        private async Task<AuthenticationResult> GetTokenAsync(PartnerCenterCredential config)
        {
            var app = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                .WithClientSecret(config.ClientSecret)
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