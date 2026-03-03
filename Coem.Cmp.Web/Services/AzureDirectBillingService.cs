using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace Coem.Cmp.Web.Services
{
    public interface IAzureDirectBillingService
    {
        Task<int> SyncDirectSubscriptionsAsync();
        // Aquí agregaremos luego el método para extraer el Usage diario de los EA
    }

    public class AzureDirectBillingService : IAzureDirectBillingService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDataProtector _protector;

        public AzureDirectBillingService(ApplicationDbContext context, IHttpClientFactory httpClientFactory, IDataProtectionProvider dataProtectionProvider)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            // Usamos el mismo protector para mantener la compatibilidad criptográfica
            _protector = dataProtectionProvider.CreateProtector("Coem.Cmp.RegionalSecrets.v1");
        }

        public async Task<int> SyncDirectSubscriptionsAsync()
        {
            var directConfigs = await _context.AzureDirectCredentials.Where(c => c.IsActive).ToListAsync();

            if (!directConfigs.Any())
            {
                Console.WriteLine("[DIRECT-ARM] No hay credenciales de EA o Patrocinio configuradas.");
                return 0;
            }

            int processedCount = 0;

            foreach (var config in directConfigs)
            {
                Console.WriteLine($"[DIRECT-ARM] Auditando entorno: {config.Alias} (Tenant: {config.TenantId})");

                try
                {
                    // 1. Autenticación Directa contra ARM (Bypass de CSP)
                    var plainTextSecret = _protector.Unprotect(config.ClientSecret);
                    var app = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                        .WithClientSecret(plainTextSecret)
                        .WithAuthority(new Uri($"https://login.microsoftonline.com/{config.TenantId}"))
                        .Build();

                    // Scope exclusivo para gestionar recursos y facturación directa
                    string[] scopes = new string[] { "https://management.azure.com/.default" };
                    var authResult = await app.AcquireTokenForClient(scopes).ExecuteAsync();

                    var client = _httpClientFactory.CreateClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    // 2. Extraer el inventario de Suscripciones Directas
                    var response = await client.GetAsync("https://management.azure.com/subscriptions?api-version=2022-12-01");

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[ERROR DIRECT-ARM] Fallo al consultar {config.Alias}: HTTP {response.StatusCode}");
                        continue;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    var armData = JsonDocument.Parse(content);

                    if (!armData.RootElement.TryGetProperty("value", out var items)) continue;

                    // 3. Emparejamiento con el Cliente en BD
                    // Buscamos si este TenantId ya existe como cliente. Si no, lo creamos como cliente "Directo/EA"
                    var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.MicrosoftTenantId.ToString() == config.TenantId);

                    if (tenant == null)
                    {
                        tenant = new Tenant
                        {
                            MicrosoftTenantId = Guid.Parse(config.TenantId),
                            Name = config.Alias, // Usamos el alias comercial
                            AgreementType = "EA/Direct", // Clasificación clave
                            Country = "Global",
                            IsBilledByCoem = false, // ¡CRÍTICO! COEM no factura esto, solo lo administra/monitorea
                            IsActive = true,
                            OnboardingDate = DateTime.UtcNow
                        };
                        _context.Tenants.Add(tenant);
                        await _context.SaveChangesAsync(); // Guardamos para obtener el ID
                    }

                    // 4. Inserción / Actualización de Suscripciones
                    foreach (var item in items.EnumerateArray())
                    {
                        var subId = Guid.Parse(item.GetProperty("subscriptionId").GetString());
                        var displayName = item.GetProperty("displayName").GetString();
                        var state = item.GetProperty("state").GetString();

                        // Clasificación Inteligente para el Dashboard
                        string categoryTag = "EA"; // Por defecto Enterprise Agreement / Direct
                        if (displayName.Contains("Sponsorship", StringComparison.OrdinalIgnoreCase) || displayName.Contains("Pass", StringComparison.OrdinalIgnoreCase))
                        {
                            categoryTag = "INT"; // Lo marcamos como Patrocinio/Interno
                        }

                        var existingSub = await _context.Subscriptions.FirstOrDefaultAsync(s => s.Id == subId);

                        if (existingSub != null)
                        {
                            existingSub.Status = state;
                            existingSub.OfferName = displayName;
                            existingSub.EffectiveDate = DateTime.UtcNow; // ARM no siempre da historial acá, usamos UTC Now
                            existingSub.Category = categoryTag;
                        }
                        else
                        {
                            _context.Subscriptions.Add(new Subscription
                            {
                                Id = subId,
                                TenantId = tenant.Id,
                                OfferId = "ARM-DIRECT", // Sello de agua para saber de dónde vino
                                OfferName = displayName,
                                Category = categoryTag,
                                CreatedDate = DateTime.UtcNow,
                                EffectiveDate = DateTime.UtcNow,
                                Status = state
                            });
                        }
                        processedCount++;
                    }
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR CRITICO ARM] {config.Alias}: {ex.Message}");
                }
            }
            return processedCount;
        }
    }
}