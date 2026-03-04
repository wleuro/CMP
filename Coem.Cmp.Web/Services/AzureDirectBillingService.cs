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
        Task SyncNightlyUsageAsync();
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
            _protector = dataProtectionProvider.CreateProtector("Coem.Cmp.RegionalSecrets.v1");
        }

        // 1. MOTOR DE DESCUBRIMIENTO Y AUDITORÍA
        public async Task<int> SyncDirectSubscriptionsAsync()
        {
            var directConfigs = await _context.AzureDirectCredentials.Where(c => c.IsActive).ToListAsync();

            if (!directConfigs.Any())
            {
                Console.WriteLine("[DIRECT-ARM] No hay credenciales configuradas.");
                return 0;
            }

            int processedCount = 0;

            foreach (var config in directConfigs)
            {
                Console.WriteLine($"[DIRECT-ARM] Auditando entorno: {config.Alias}");

                try
                {
                    var client = await GetAuthorizedClient(config);
                    var response = await client.GetAsync("https://management.azure.com/subscriptions?api-version=2022-12-01");

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[ERROR DIRECT-ARM] Fallo al consultar {config.Alias}: HTTP {response.StatusCode}");
                        continue;
                    }

                    var armData = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (!armData.RootElement.TryGetProperty("value", out var items)) continue;

                    foreach (var item in items.EnumerateArray())
                    {
                        var subId = Guid.Parse(item.GetProperty("subscriptionId").GetString());
                        var displayName = item.GetProperty("displayName").GetString();
                        var state = item.GetProperty("state").GetString();

                        // --- PRUEBA ÁCIDA DE PERMISOS ---
                        var costPing = await client.GetAsync($"https://management.azure.com/subscriptions/{subId}/providers/Microsoft.Consumption/usageDetails?$top=1&api-version=2023-11-01");
                        var readPing = await client.GetAsync($"https://management.azure.com/subscriptions/{subId}/resources?$top=1&api-version=2021-04-01");
                        var auditStr = $"Cost:{(costPing.IsSuccessStatusCode ? "OK" : "FAIL")}|Read:{(readPing.IsSuccessStatusCode ? "OK" : "FAIL")}";

                        // --- GUARDADO EN EL SILO EXTERNO ---
                        var existingSub = await _context.ExternalSubscriptions.FirstOrDefaultAsync(s => s.Id == subId);

                        if (existingSub == null)
                        {
                            _context.ExternalSubscriptions.Add(new ExternalSubscription
                            {
                                Id = subId,
                                AzureDirectCredentialId = config.Id,
                                Name = displayName,
                                Status = state,
                                AuditResult = auditStr,
                                LastSync = DateTime.UtcNow,
                                Markup = 0.00m // Margen inicial en 0%
                            });
                        }
                        else
                        {
                            existingSub.AuditResult = auditStr;
                            existingSub.Status = state;
                            existingSub.LastSync = DateTime.UtcNow;
                            // Ojo: NO sobreescribimos el Markup aquí para no borrar la configuración del TAM
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

        // 2. MOTOR FINANCIERO NOCTURNO (CON MARKUP)
        public async Task SyncNightlyUsageAsync()
        {
            var credentials = await _context.AzureDirectCredentials.Where(c => c.IsActive).ToListAsync();

            // Consumo del día anterior (cerrado oficialmente por Azure)
            var targetDate = DateTime.UtcNow.AddDays(-1).Date;

            foreach (var config in credentials)
            {
                try
                {
                    var client = await GetAuthorizedClient(config);

                    // Solo traemos las suscripciones de este conector específico
                    var subs = await _context.ExternalSubscriptions
                        .Where(s => s.AzureDirectCredentialId == config.Id)
                        .ToListAsync();

                    foreach (var sub in subs)
                    {
                        // Blindaje: Evitar colisión si por error la sub existe en CSP nativo
                        if (await _context.Subscriptions.AnyAsync(s => s.Id == sub.Id)) continue;

                        var url = $"https://management.azure.com/subscriptions/{sub.Id}/providers/Microsoft.Consumption/usageDetails?metric=AmortizedCost&$filter=properties/usageStart eq '{targetDate:yyyy-MM-dd}'&api-version=2023-11-01";
                        var response = await client.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var usageData = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                            if (usageData.RootElement.TryGetProperty("value", out var records))
                            {
                                foreach (var record in records.EnumerateArray())
                                {
                                    var props = record.GetProperty("properties");
                                    var productName = props.GetProperty("product").GetString();
                                    var rawCost = props.GetProperty("costInBillingCurrency").GetDecimal();

                                    // LA MATEMÁTICA DEL MARGEN (Tu rentabilidad en COEM)
                                    decimal calculatedBilledCost = rawCost * (1 + sub.Markup);

                                    // IDEMPOTENCIA: Verificar si ya guardamos este recurso hoy
                                    bool exists = await _context.ExternalUsageRecords.AnyAsync(u =>
                                        u.SubscriptionId == sub.Id &&
                                        u.UsageDate == targetDate &&
                                        u.ProductName == productName);

                                    if (!exists)
                                    {
                                        _context.ExternalUsageRecords.Add(new ExternalUsageRecord
                                        {
                                            SubscriptionId = sub.Id,
                                            UsageDate = targetDate,
                                            ProductName = productName,
                                            MeterCategory = props.GetProperty("meterCategory").GetString(),
                                            Quantity = props.GetProperty("quantity").GetDecimal(),
                                            EstimatedCost = rawCost,             // Lo que cobra Azure
                                            MarkupPercentage = sub.Markup,       // El % aplicado ese día
                                            BilledCost = calculatedBilledCost,   // Lo que facturas/muestras
                                            Currency = props.GetProperty("billingCurrencyCode").GetString(),
                                            ProviderSource = "BYOT_EA"
                                        });
                                    }
                                }
                                await _context.SaveChangesAsync();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NIGHTLY ERROR] {config.Alias}: {ex.Message}");
                }
            }
        }

        // HELPER: Autenticación centralizada
        private async Task<HttpClient> GetAuthorizedClient(AzureDirectCredential config)
        {
            var plainTextSecret = _protector.Unprotect(config.ClientSecret);
            var app = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                .WithClientSecret(plainTextSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{config.TenantId}"))
                .Build();

            string[] scopes = new string[] { "https://management.azure.com/.default" };
            var authResult = await app.AcquireTokenForClient(scopes).ExecuteAsync();

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }
    }
}