using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Coem.Cmp.Web.Services
{
    public interface IAzureDirectBillingService
    {
        Task<int> SyncDirectSubscriptionsAsync();
        Task SyncDailyConsumptionAsync(); // Renombrado para mayor claridad semántica
    }

    public class AzureDirectBillingService : IAzureDirectBillingService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDataProtector _protector;
        private readonly ILogger<AzureDirectBillingService> _logger;

        public AzureDirectBillingService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<AzureDirectBillingService> logger)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(httpClientFactory);
            ArgumentNullException.ThrowIfNull(dataProtectionProvider);
            ArgumentNullException.ThrowIfNull(logger);

            _context = context;
            _httpClientFactory = httpClientFactory;
            _protector = dataProtectionProvider.CreateProtector("Coem.Cmp.RegionalSecrets.v1");
            _logger = logger;
        }

        // 1. MOTOR DE DESCUBRIMIENTO Y AUDITORÍA
        public async Task<int> SyncDirectSubscriptionsAsync()
        {
            var directConfigs = await _context.AzureDirectCredentials.Where(c => c.IsActive).ToListAsync();

            if (!directConfigs.Any())
            {
                _logger.LogInformation("[DIRECT-ARM] No hay credenciales configuradas.");
                return 0;
            }

            int processedCount = 0;

            foreach (var config in directConfigs)
            {
                _logger.LogInformation($"[DIRECT-ARM] Auditando entorno: {config.Alias}");

                try
                {
                    var client = await GetAuthorizedClient(config);
                    var response = await client.GetAsync("https://management.azure.com/subscriptions?api-version=2022-12-01");

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"[ERROR DIRECT-ARM] Fallo al consultar {config.Alias}: HTTP {response.StatusCode}");
                        continue;
                    }

                    var armData = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (!armData.RootElement.TryGetProperty("value", out var items)) continue;

                    foreach (var item in items.EnumerateArray())
                    {
                        var subIdStr = item.GetProperty("subscriptionId").GetString();
                        if (string.IsNullOrEmpty(subIdStr)) continue;

                        var subId = Guid.Parse(subIdStr);
                        var displayName = item.GetProperty("displayName").GetString() ?? "Unknown";
                        var state = item.GetProperty("state").GetString() ?? "Unknown";

                        // --- PRUEBA ÁCIDA DE PERMISOS ---
                        var costPing = await client.GetAsync($"https://management.azure.com/subscriptions/{subIdStr}/providers/Microsoft.Consumption/usageDetails?$top=1&api-version=2023-11-01");
                        var readPing = await client.GetAsync($"https://management.azure.com/subscriptions/{subIdStr}/resources?$top=1&api-version=2021-04-01");
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
                    _logger.LogCritical($"[ERROR CRITICO ARM] {config.Alias}: {ex.Message}");
                }
            }
            return processedCount;
        }

        // 2. MOTOR FINANCIERO EN TIEMPO REAL (El "Radar" Operativo)
        public async Task SyncDailyConsumptionAsync()
        {
            var credentials = await _context.AzureDirectCredentials.Where(c => c.IsActive).ToListAsync();

            // Definimos el mes en curso para el backfill operativo
            var today = DateTime.UtcNow;
            var startOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            foreach (var config in credentials)
            {
                try
                {
                    _logger.LogInformation($"[DIRECT-ARM] Iniciando extracción de consumo para entorno: {config.Alias}");
                    var client = await GetAuthorizedClient(config);

                    var subs = await _context.ExternalSubscriptions
                        .Where(s => s.AzureDirectCredentialId == config.Id)
                        .ToListAsync();

                    foreach (var sub in subs)
                    {
                        // Escudo de colisión: Si la suscripción ya está en Partner Center, priorizamos esa vía
                        if (await _context.Subscriptions.AnyAsync(s => s.Id == sub.Id)) continue;

                        _logger.LogInformation($"[DIRECT-ARM] Limpiando consumo previo del mes para suscripción: {sub.Id}");
                        // WIPE AND REPLACE: Borramos el consumo de este mes para inyectar el acumulado fresco
                        await _context.ExternalUsageRecords
                            .Where(r => r.SubscriptionId == sub.Id && r.UsageDate >= startOfMonth)
                            .ExecuteDeleteAsync();

                        // Llamada a la API de Cost Management filtrando desde el inicio del mes
                        var url = $"https://management.azure.com/subscriptions/{sub.Id}/providers/Microsoft.Consumption/usageDetails?metric=AmortizedCost&$filter=properties/usageStart ge '{startOfMonth:yyyy-MM-dd}'&api-version=2023-11-01";
                        var response = await client.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var usageData = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                            if (usageData.RootElement.TryGetProperty("value", out var records))
                            {
                                var recordsBatch = new List<ExternalUsageRecord>();
                                int batchSize = 1000;

                                foreach (var record in records.EnumerateArray())
                                {
                                    var props = record.GetProperty("properties");
                                    var productName = props.GetProperty("product").GetString() ?? "Unknown";
                                    var rawCost = props.TryGetProperty("costInBillingCurrency", out var costEl) && costEl.ValueKind != JsonValueKind.Null ? costEl.GetDecimal() : 0m;
                                    var usageDateStr = props.GetProperty("usageStart").GetString();
                                    var recordDate = !string.IsNullOrEmpty(usageDateStr) ? DateTime.Parse(usageDateStr) : DateTime.UtcNow;

                                    var meterCategory = props.TryGetProperty("meterCategory", out var catEl) ? catEl.GetString() ?? "Unknown" : "Unknown";
                                    var billingCurrency = props.TryGetProperty("billingCurrencyCode", out var curEl) ? catEl.GetString() ?? "USD" : "USD";

                                    decimal calculatedBilledCost = rawCost * (1 + sub.Markup);

                                    recordsBatch.Add(new ExternalUsageRecord
                                    {
                                        SubscriptionId = sub.Id,
                                        UsageDate = recordDate,
                                        ProductName = productName,
                                        MeterCategory = meterCategory,
                                        Quantity = props.TryGetProperty("quantity", out var qtyEl) && qtyEl.ValueKind != JsonValueKind.Null ? qtyEl.GetDecimal() : 0m,
                                        EstimatedCost = rawCost,
                                        MarkupPercentage = sub.Markup,
                                        BilledCost = calculatedBilledCost,
                                        Currency = billingCurrency,
                                        ProviderSource = "BYOT_EA_Direct"
                                    });

                                    if (recordsBatch.Count >= batchSize)
                                    {
                                        _context.ExternalUsageRecords.AddRange(recordsBatch);
                                        await _context.SaveChangesAsync();
                                        recordsBatch.Clear();
                                    }
                                }

                                if (recordsBatch.Any())
                                {
                                    _context.ExternalUsageRecords.AddRange(recordsBatch);
                                    await _context.SaveChangesAsync();
                                }
                                _logger.LogInformation($"[DIRECT-ARM] Inyectado consumo del mes para suscripción: {sub.Id}");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"[DIRECT-ARM] API Cost Management rechazó la suscripción {sub.Id}. HTTP: {response.StatusCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[ERROR CRITICO ARM] Fallo en la extracción para {config.Alias}: {ex.Message}");
                }
            }
        }

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