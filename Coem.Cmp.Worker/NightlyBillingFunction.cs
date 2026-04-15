using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Coem.Cmp.Web.Services;

namespace Coem.Cmp.Worker
{
    public class NightlyBillingFunction
    {
        private readonly ILogger<NightlyBillingFunction> _logger;
        private readonly IPartnerCenterSyncService _pcSyncService;
        private readonly IAzureDirectBillingService _azureDirectService;

        // Inyección de dependencias de todos los motores
        public NightlyBillingFunction(
            ILogger<NightlyBillingFunction> logger,
            IPartnerCenterSyncService pcSyncService,
            IAzureDirectBillingService azureDirectService)
        {
            _logger = logger;
            _pcSyncService = pcSyncService;
            _azureDirectService = azureDirectService;
        }

        [Function("SyncDailyUsage")] // Renombrado a uso diario para reflejar el modelo híbrido
        public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation($"[ORQUESTADOR HÍBRIDO] Iniciando motor automatizado a las: {DateTime.UtcNow} UTC");

            try
            {
                // ==========================================================
                // VÍA 1: LA VERDAD FINANCIERA (Partner Center & Graph API)
                // ==========================================================
                _logger.LogInformation("Fase 1: Sincronizando clientes y suscripciones CSP...");
                await _pcSyncService.SyncCustomersAsync();
                await _pcSyncService.SyncSubscriptionsAsync();

                _logger.LogInformation("Fase 2: Extrayendo facturación financiera (Graph)...");
                // Intentamos traer la verdad contable. Si Microsoft lo bloquea por cierre, el log lo reportará silenciosamente.
                await _pcSyncService.SyncNightlyUsageAsync("current");


                // ==========================================================
                // VÍA 1.5: EL RADAR OPERATIVO CSP (El Torniquete de Tiempo Real)
                // ==========================================================
                _logger.LogInformation("Fase 2.5: Extrayendo consumo operativo (Radar CSP) vía Cost Management...");
                // Esta es la línea vital que salva la visibilidad durante los 15 días de apagón de Microsoft
                await _pcSyncService.SyncCspOperationalConsumptionAsync();


                // ==========================================================
                // VÍA 2: EL RADAR EXTERNO/BYOT (Azure Cost Management Directo)
                // ==========================================================
                _logger.LogInformation("Fase 3: Sincronizando metadatos de entornos directos (Azure Direct)...");
                await _azureDirectService.SyncDirectSubscriptionsAsync();

                _logger.LogInformation("Fase 4: Extrayendo consumo operativo externo acumulado del mes...");
                await _azureDirectService.SyncDailyConsumptionAsync();


                _logger.LogInformation("[ORQUESTADOR HÍBRIDO] Ciclo de facturación completado con éxito.");
            }
            catch (Exception ex)
            {
                // Propagación crítica para que Application Insights y Azure registren la caída
                _logger.LogError($"Fallo crítico en la ejecución del orquestador: {ex.Message}");
                throw;
            }
        }
    }
}