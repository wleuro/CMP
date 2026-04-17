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

        public NightlyBillingFunction(
            ILogger<NightlyBillingFunction> logger,
            IPartnerCenterSyncService pcSyncService,
            IAzureDirectBillingService azureDirectService)
        {
            _logger = logger;
            _pcSyncService = pcSyncService;
            _azureDirectService = azureDirectService;
        }

        [Function("SyncDailyUsage")]
        public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation($"[ORQUESTADOR HÍBRIDO ZENITH] Iniciando ciclo de ejecución: {DateTime.UtcNow} UTC");

            try
            {
                // ==========================================================
                // FASE 1: INFRAESTRUCTURA DE CLIENTES
                // ==========================================================
                _logger.LogInformation("Fase 1: Sincronizando metadatos de clientes CSP...");
                await _pcSyncService.SyncCustomersAsync();

                // ==========================================================
                // FASE 1.5: MÓDULO SaaS (M365, Copilot, Dynamics)
                // ==========================================================
                _logger.LogInformation("Fase 1.5: Extrayendo inventario de licencias SaaS...");
                // Este motor es vital para el flujo de caja recurrente de Coem
                await _pcSyncService.SyncSaaSSubscriptionsAsync();

                // ==========================================================
                // FASE 2: CONSUMO AZURE - VÍA FINANCIERA (Graph API)
                // ==========================================================
                _logger.LogInformation("Fase 2: Sincronizando suscripciones Azure y facturación oficial...");
                await _pcSyncService.SyncSubscriptionsAsync();

                // Intento de descarga de archivos NCE. Maneja el 'limbo' de inicio de mes automáticamente.
                await _pcSyncService.SyncNightlyUsageAsync("current");

                // ==========================================================
                // FASE 2.5: CONSUMO AZURE - VÍA OPERATIVA (Radar CSP)
                // ==========================================================
                _logger.LogInformation("Fase 2.5: Extrayendo consumo en tiempo real (Torniquete CSP)...");
                // Esta fase salva la visibilidad durante el cierre de Microsoft
                await _pcSyncService.SyncCspOperationalConsumptionAsync();

                // ==========================================================
                // FASE 3 y 4: ENTORNOS EXTERNOS / BYOT (Azure Direct)
                // ==========================================================
                _logger.LogInformation("Fase 3: Sincronizando metadatos de entornos directos...");
                await _azureDirectService.SyncDirectSubscriptionsAsync();

                _logger.LogInformation("Fase 4: Extrayendo consumo operativo de entornos externos...");
                await _azureDirectService.SyncDailyConsumptionAsync();

                _logger.LogInformation("[ORQUESTADOR HÍBRIDO] Ciclo completado con éxito.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FALLO CRÍTICO] El orquestador se detuvo: {ex.Message}");
                // Re-lanzamos para que Azure Functions registre el reintento o la falla en el portal
                throw;
            }
        }
    }
}