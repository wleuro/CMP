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
        Task SyncCspOperationalConsumptionAsync(); // EL NUEVO RADAR CSP
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
                    var authResult = await GetTokenAsync(config, isGraph: false);
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
                                var companyName = item.GetProperty("companyProfile").GetProperty("companyName").GetString() ?? "Unknown";
                                var domain = item.GetProperty("companyProfile").GetProperty("domain").GetString() ?? "unknown.onmicrosoft.com";

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

        // --- SINCRONIZACIÓN DE SUSCRIPCIONES (Parametrizada por BD) ---
        public async Task<int> SyncSubscriptionsAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();

            var categoryRules = await _context.CategoryMappings
                .Where(c => c.IsActive)
                .OrderBy(c => c.Priority)
                .AsNoTracking()
                .ToListAsync();

            int processedCount = 0;

            foreach (var config in regionalConfigs)
            {
                try
                {
                    var authResult = await GetTokenAsync(config, isGraph: false);
                    var client = CreateHttpClient(authResult.AccessToken);
                    var tenants = await _context.Tenants.Where(t => t.Country == config.CountryName).ToListAsync();

                    _logger.LogInformation($"Buscando suscripciones para {tenants.Count} tenants en {config.CountryName}...");

                    foreach (var tenant in tenants)
                    {
                        string url = $"https://api.partnercenter.microsoft.com/v1/customers/{tenant.MicrosoftTenantId}/subscriptions";
                        var response = await client.GetAsync(url);

                        if (!response.IsSuccessStatusCode)
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            _logger.LogWarning($"[BLOQUEO] Tenant {tenant.MicrosoftTenantId} ({config.CountryName}). Código: {response.StatusCode}. Detalle: {errorContent}");
                            continue;
                        }

                        var content = await response.Content.ReadAsStringAsync();
                        var pcData = JsonDocument.Parse(content);
                        if (!pcData.RootElement.TryGetProperty("items", out var items)) continue;

                        foreach (var item in items.EnumerateArray())
                        {
                            var subIdStr = item.GetProperty("id").GetString();
                            if (string.IsNullOrEmpty(subIdStr)) continue;

                            var subId = Guid.Parse(subIdStr);
                            var offerName = item.GetProperty("offerName").GetString() ?? "Unknown";
                            var offerId = item.GetProperty("offerId").GetString() ?? "Unknown";

                            string categoryTag = "AL";
                            string offerNameUpper = offerName.ToUpperInvariant();
                            string offerIdUpper = offerId.ToUpperInvariant();

                            foreach (var rule in categoryRules)
                            {
                                string ruleKeyword = rule.Keyword.ToUpperInvariant();
                                if (offerNameUpper.Contains(ruleKeyword) || offerIdUpper.Contains(ruleKeyword))
                                {
                                    categoryTag = rule.CategoryCode;
                                    break;
                                }
                            }

                            var existingSub = await _context.Subscriptions.FirstOrDefaultAsync(s => s.Id == subId);
                            if (existingSub != null)
                            {
                                var status = item.GetProperty("status").GetString() ?? existingSub.Status;
                                existingSub.Status = status;
                                existingSub.OfferName = offerName;
                                existingSub.Category = categoryTag;
                            }
                            else
                            {
                                DateTime? effDate = null;
                                if (item.TryGetProperty("effectiveStartDate", out var dateEl) && dateEl.ValueKind != JsonValueKind.Null)
                                {
                                    effDate = dateEl.GetDateTime();
                                }

                                var statusStr = item.GetProperty("status").GetString() ?? "Unknown";
                                var createdDateStr = item.GetProperty("creationDate").GetString();
                                var createdDate = !string.IsNullOrEmpty(createdDateStr) ? DateTime.Parse(createdDateStr) : DateTime.UtcNow;

                                _context.Subscriptions.Add(new Subscription
                                {
                                    Id = subId,
                                    TenantId = tenant.Id,
                                    OfferId = offerId,
                                    OfferName = offerName,
                                    Category = categoryTag,
                                    CreatedDate = createdDate,
                                    EffectiveDate = effDate,
                                    Status = statusStr,
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

        // --- MOTOR NCE ASÍNCRONO (Con Cortacircuitos Resiliente) ---
        public async Task SyncNightlyUsageAsync(string? billingPeriod = "current")
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();

            foreach (var config in regionalConfigs)
            {
                try
                {
                    _logger.LogInformation($"Solicitando exportación NCE ({billingPeriod}) vía Graph para {config.CountryName}...");

                    var authResult = await GetTokenAsync(config, isGraph: true);
                    var client = CreateHttpClient(authResult.AccessToken);

                    string exportType = billingPeriod == "current" ? "unbilled" : "billed";
                    string url = $"https://graph.microsoft.com/v1.0/reports/partners/billing/usage/{exportType}/export";

                    var requestBody = new { billingPeriod = billingPeriod, currencyCode = "USD", attributeSet = "full" };
                    var response = await client.PostAsJsonAsync(url, requestBody);

                    if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                    {
                        var locationHeader = response.Headers.Location;
                        if (locationHeader == null) continue;

                        string operationUrl = locationHeader.ToString();
                        bool isCompleted = false;
                        int attemptCounter = 0;
                        int maxAttempts = 30; // 15 minutos máximo de espera por país

                        while (!isCompleted && attemptCounter < maxAttempts)
                        {
                            attemptCounter++;
                            await Task.Delay(30000);

                            var statusResponse = await client.GetAsync(operationUrl);
                            if (!statusResponse.IsSuccessStatusCode)
                            {
                                _logger.LogWarning($"[BLOQUEO HTTP] Graph falló para {config.CountryName}. Código: {statusResponse.StatusCode}. Reintentando...");
                                continue;
                            }

                            var contentString = await statusResponse.Content.ReadAsStringAsync();
                            using var statusData = JsonDocument.Parse(contentString);
                            var root = statusData.RootElement;

                            string rawStatus = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "unknown" : "unknown";
                            string statusLower = rawStatus.ToLowerInvariant();

                            _logger.LogInformation($"[{config.CountryName} Intento {attemptCounter}/{maxAttempts}] Estado reportado por Microsoft: '{rawStatus}'");

                            if (statusLower == "completed" || statusLower == "succeeded")
                            {
                                await ProcessManifestFiles(root.Clone(), config, billingPeriod!);
                                isCompleted = true;
                            }
                            else if (statusLower == "failed")
                            {
                                _logger.LogError($"[ERROR GRAVE] La exportación falló en los servidores de Microsoft para {config.CountryName}. Detalle: {contentString}");
                                break;
                            }
                        }

                        if (!isCompleted)
                        {
                            _logger.LogWarning($"[TIMEOUT NCE] Se abortó la espera para {config.CountryName} tras 15 minutos. Posible falta de facturación activa en Microsoft.");
                        }
                    }
                    else
                    {
                        string errorStr = await response.Content.ReadAsStringAsync();
                        _logger.LogError($"[RECHAZO INICIAL] No se pudo crear la solicitud en Graph para {config.CountryName}. HTTP {response.StatusCode}: {errorStr}");
                    }
                }
                catch (Exception ex) { _logger.LogError($"[FATAL NCE] Error en {config.CountryName}: {ex.Message}"); }
            }
        }

        // --- VÍA OPERATIVA: BYPASS CSP POR COST MANAGEMENT (EL RADAR CSP) ---
        public async Task SyncCspOperationalConsumptionAsync()
        {
            var regionalConfigs = await _context.PartnerCenterCredentials.Where(c => c.IsActive).ToListAsync();
            var today = DateTime.UtcNow;
            var startOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            foreach (var config in regionalConfigs)
            {
                _logger.LogInformation($"[RADAR CSP] Iniciando bypass operativo Cost Management para {config.CountryName}");
                try
                {
                    // 1. Token para Azure Resource Manager
                    var authResult = await GetArmTokenAsync(config);
                    var client = CreateHttpClient(authResult.AccessToken);

                    // 2. Descubrir el BillingAccountId dinámicamente
                    var billingAccountsResponse = await client.GetAsync("https://management.azure.com/providers/Microsoft.Billing/billingAccounts?api-version=2020-05-01");
                    if (!billingAccountsResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning($"[RADAR CSP] Sin acceso a BillingAccounts en {config.CountryName}. HTTP {billingAccountsResponse.StatusCode}. ¿Tiene el AppReg el rol de 'Billing Reader' en el tenant?");
                        continue;
                    }

                    var baDoc = JsonDocument.Parse(await billingAccountsResponse.Content.ReadAsStringAsync());
                    if (!baDoc.RootElement.TryGetProperty("value", out var baArray) || baArray.GetArrayLength() == 0) continue;

                    string billingAccountId = baArray[0].GetProperty("name").GetString() ?? "";
                    _logger.LogInformation($"[RADAR CSP] Raíz de Facturación detectada: {billingAccountId}");

                    // 3. Recorrer los Tenants CSP de este país
                    var tenants = await _context.Tenants.Where(t => t.Country == config.CountryName).ToListAsync();

                    foreach (var tenant in tenants)
                    {
                        _logger.LogInformation($"[RADAR CSP] Extrayendo datos operativos del mes para Tenant {tenant.MicrosoftTenantId}");

                        // Wipe and Replace: Limpiamos los datos operativos previos de este mes
                        await _context.PCUsageRecords
                            .Where(r => r.TenantId == tenant.Id && r.UsageDate >= startOfMonth && r.ProviderSource == "CostManagement_Operational")
                            .ExecuteDeleteAsync();

                        // 4. Consulta a la API
                        string scope = $"providers/Microsoft.Billing/billingAccounts/{billingAccountId}/customers/{tenant.MicrosoftTenantId}";
                        string url = $"https://management.azure.com/{scope}/providers/Microsoft.Consumption/usageDetails?metric=AmortizedCost&$filter=properties/usageStart ge '{startOfMonth:yyyy-MM-dd}'&api-version=2023-11-01";

                        var usageResponse = await client.GetAsync(url);
                        if (!usageResponse.IsSuccessStatusCode) continue;

                        var usageDoc = JsonDocument.Parse(await usageResponse.Content.ReadAsStringAsync());
                        if (!usageDoc.RootElement.TryGetProperty("value", out var records)) continue;

                        var recordsBatch = new List<PCUsageRecord>();
                        int processed = 0;

                        foreach (var record in records.EnumerateArray())
                        {
                            var props = record.GetProperty("properties");
                            var usageDateStr = props.TryGetProperty("usageStart", out var dEl) && dEl.ValueKind != JsonValueKind.Null ? dEl.GetString() : null;
                            var date = !string.IsNullOrEmpty(usageDateStr) ? DateTime.Parse(usageDateStr) : DateTime.UtcNow;

                            recordsBatch.Add(new PCUsageRecord
                            {
                                TenantId = tenant.Id,
                                SubscriptionId = props.TryGetProperty("subscriptionId", out var sEl) && sEl.ValueKind != JsonValueKind.Null ? Guid.Parse(sEl.GetString()!) : Guid.Empty,
                                UsageDate = date,
                                Publisher = "Microsoft",
                                ChargeType = "Usage",
                                ProductName = props.TryGetProperty("product", out var pEl) ? pEl.GetString() ?? "Unknown" : "Unknown",
                                MeterCategory = props.TryGetProperty("meterCategory", out var mEl) ? mEl.GetString() ?? "Unknown" : "Unknown",
                                Quantity = props.TryGetProperty("quantity", out var qEl) && qEl.ValueKind != JsonValueKind.Null ? qEl.GetDecimal() : 0m,
                                EstimatedCost = props.TryGetProperty("costInBillingCurrency", out var cEl) && cEl.ValueKind != JsonValueKind.Null ? cEl.GetDecimal() : 0m,
                                BilledCost = props.TryGetProperty("costInBillingCurrency", out var bcEl) && bcEl.ValueKind != JsonValueKind.Null ? bcEl.GetDecimal() : 0m,
                                MarkupPercentage = 0m,
                                Currency = props.TryGetProperty("billingCurrencyCode", out var curEl) ? curEl.GetString() ?? "USD" : "USD",
                                ProviderSource = "CostManagement_Operational", // LA MARCA DE AGUA
                                ResourceId = props.TryGetProperty("instanceId", out var iEl) && iEl.ValueKind != JsonValueKind.Null ? iEl.GetString() : null
                            });

                            processed++;
                            if (recordsBatch.Count >= 1000)
                            {
                                _context.PCUsageRecords.AddRange(recordsBatch);
                                await _context.SaveChangesAsync();
                                recordsBatch.Clear();
                            }
                        }

                        if (recordsBatch.Any())
                        {
                            _context.PCUsageRecords.AddRange(recordsBatch);
                            await _context.SaveChangesAsync();
                        }

                        if (processed > 0) _logger.LogInformation($"[RADAR CSP] {processed} registros de consumo en tiempo real inyectados para el cliente {tenant.Name}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[ERROR RADAR CSP] Fallo en la extracción operativa para {config.CountryName}: {ex.Message}");
                }
            }
        }

        private async Task ProcessManifestFiles(JsonElement statusData, PartnerCenterCredential config, string billingPeriod)
        {
            _logger.LogInformation($"Iniciando extracción de facturación masiva NCE ({billingPeriod}) para {config.CountryName}...");

            try
            {
                if (!statusData.TryGetProperty("manifest", out var manifest) || !manifest.TryGetProperty("blobs", out var blobs))
                {
                    _logger.LogWarning($"Microsoft no devolvió blobs de datos en el manifiesto para {config.CountryName}.");
                    return;
                }

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(15);

                // PARCHE TÁCTICO: Diccionario en minúsculas estrictas
                var tenantCache = await _context.Tenants
                    .Where(t => t.Country == config.CountryName)
                    .ToDictionaryAsync(t => t.MicrosoftTenantId.ToString().ToLowerInvariant(), t => t.Id);

                DateTime startRange;
                DateTime endRange;

                if (billingPeriod == "current")
                {
                    startRange = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    endRange = DateTime.UtcNow.AddDays(1);
                }
                else
                {
                    int year = int.Parse(billingPeriod.Substring(0, 4));
                    int month = int.Parse(billingPeriod.Substring(4, 2));
                    startRange = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                    endRange = startRange.AddMonths(1).AddTicks(-1);
                }

                _logger.LogInformation($"Limpiando colisiones para el periodo {billingPeriod} en {config.CountryName}...");

                var tenantIds = tenantCache.Values.ToList();
                await _context.PCUsageRecords
                    .Where(r => tenantIds.Contains(r.TenantId) && r.UsageDate >= startRange && r.UsageDate <= endRange)
                    .ExecuteDeleteAsync();

                _logger.LogInformation($"Purga completada. Iniciando inyección de datos frescos...");

                foreach (var blob in blobs.EnumerateArray())
                {
                    string downloadUrl = blob.GetProperty("url").GetString() ?? string.Empty;
                    if (string.IsNullOrEmpty(downloadUrl)) continue;

                    _logger.LogInformation($"Descargando bloque de datos particionado para {config.CountryName}...");

                    using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"Fallo la descarga para {config.CountryName}. Status HTTP: {response.StatusCode}");
                        continue;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var reader = new System.IO.StreamReader(stream);

                    string? line;
                    int batchSize = 1000;
                    var recordsBatch = new List<PCUsageRecord>();
                    int totalProcessed = 0;
                    bool isFirstLine = true;

                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        // LOG FRANCOTIRADOR
                        if (isFirstLine)
                        {
                            _logger.LogWarning($"[ANATOMÍA JSON] Primer registro detectado: {line.Substring(0, Math.Min(line.Length, 300))}...");
                            isFirstLine = false;
                        }

                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        // SANITIZACIÓN: Todo a minúsculas antes de buscar
                        string msTenantId = root.TryGetProperty("customerId", out var cId) ? cId.GetString() ?? "" : "";
                        msTenantId = msTenantId.ToLowerInvariant();

                        if (!tenantCache.TryGetValue(msTenantId, out int internalTenantId))
                        {
                            continue; // Ignoramos si realmente no es nuestro cliente
                        }

                        var record = new PCUsageRecord
                        {
                            TenantId = internalTenantId,
                            SubscriptionId = root.TryGetProperty("subscriptionId", out var sId) && sId.ValueKind != JsonValueKind.Null ? Guid.Parse(sId.GetString()!) : Guid.Empty,

                            UsageDate = root.TryGetProperty("date", out var dateEl) && dateEl.ValueKind != JsonValueKind.Null ? dateEl.GetDateTime() :
                                        (root.TryGetProperty("chargeStartDate", out var cDateEl) && cDateEl.ValueKind != JsonValueKind.Null ? cDateEl.GetDateTime() : DateTime.UtcNow),

                            Publisher = root.TryGetProperty("publisherName", out var pubEl) ? pubEl.GetString() ?? "Microsoft" : "Microsoft",
                            ChargeType = root.TryGetProperty("chargeType", out var chargeEl) ? chargeEl.GetString() ?? "Usage" : "Usage",

                            ProductName = root.TryGetProperty("productName", out var prodEl) ? prodEl.GetString() ?? "Unknown" : "Unknown",
                            MeterCategory = root.TryGetProperty("meterCategory", out var catEl) ? catEl.GetString() ?? "N/A" : "N/A",

                            Quantity = root.TryGetProperty("quantity", out var qtyEl) && qtyEl.ValueKind != JsonValueKind.Null ? qtyEl.GetDecimal() : 0m,

                            EstimatedCost = root.TryGetProperty("billingPreTaxTotal", out var preTaxEl) && preTaxEl.ValueKind != JsonValueKind.Null ? preTaxEl.GetDecimal() : 0m,
                            BilledCost = root.TryGetProperty("billingPreTaxTotal", out var preTaxEl2) && preTaxEl2.ValueKind != JsonValueKind.Null ? preTaxEl2.GetDecimal() : 0m,
                            MarkupPercentage = 0m,

                            Currency = root.TryGetProperty("currency", out var currEl) ? currEl.GetString() ?? "USD" : "USD",
                            ProviderSource = "PartnerCenter_NCE_Graph",

                            ResourceId = root.TryGetProperty("resourceId", out var resIdEl) && resIdEl.ValueKind != JsonValueKind.Null ? resIdEl.GetString() : null,
                            ResourceName = root.TryGetProperty("resourceName", out var resNameEl) && resNameEl.ValueKind != JsonValueKind.Null ? resNameEl.GetString() : null,
                            TagsJson = root.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind != JsonValueKind.Null ? tagsEl.GetRawText() : null
                        };

                        recordsBatch.Add(record);
                        totalProcessed++;

                        if (recordsBatch.Count >= batchSize)
                        {
                            _context.PCUsageRecords.AddRange(recordsBatch);
                            await _context.SaveChangesAsync();
                            recordsBatch.Clear();
                        }
                    }

                    if (recordsBatch.Any())
                    {
                        _context.PCUsageRecords.AddRange(recordsBatch);
                        await _context.SaveChangesAsync();
                    }

                    _logger.LogInformation($"[ÉXITO] Extracción NCE completada para {config.CountryName}. {totalProcessed} registros almacenados en silos FinOps.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Fallo de infraestructura en la extracción asíncrona para {config.CountryName}: {ex.Message}");
            }
        }

        private async Task<AuthenticationResult> GetTokenAsync(PartnerCenterCredential config, bool isGraph)
        {
            var plainTextSecret = _protector.Unprotect(config.ClientSecret);
            var app = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                .WithClientSecret(plainTextSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{config.TenantId}"))
                .Build();

            string scope = isGraph ? "https://graph.microsoft.com/.default" : "https://api.partnercenter.microsoft.com/.default";
            return await app.AcquireTokenForClient(new[] { scope }).ExecuteAsync();
        }

        // Método de soporte para sacar el token directo de Azure Resource Manager
        private async Task<AuthenticationResult> GetArmTokenAsync(PartnerCenterCredential config)
        {
            var plainTextSecret = _protector.Unprotect(config.ClientSecret);
            var app = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                .WithClientSecret(plainTextSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{config.TenantId}"))
                .Build();

            return await app.AcquireTokenForClient(new[] { "https://management.azure.com/.default" }).ExecuteAsync();
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