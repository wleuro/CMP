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
        private readonly IAzureDirectBillingService _directBillingService;
        private readonly IPartnerCenterSyncService _pcSyncService;

        public NightlyBillingFunction(ILogger<NightlyBillingFunction> logger,
                                      IAzureDirectBillingService directBillingService,
                                      IPartnerCenterSyncService pcSyncService)
        {
            _logger = logger;
            _directBillingService = directBillingService;
            _pcSyncService = pcSyncService;
        }

        // CRON AJUSTADO: "0 0 2 * * *" (Se ejecuta todos los días a las 2:00 AM UTC)
        // Si necesitas probar localmente, coméntalo y usa "0 */5 * * * *" (cada 5 minutos máximo)
        [Function("SyncNightlyUsage")]
        public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation($"[ZENITH] Motor FinOps despertando a las: {DateTime.UtcNow} UTC");

            // --- BLOQUE 1: CSP NCE ---
            try
            {
                _logger.LogInformation($"[ZENITH] Iniciando extracción nativa CSP NCE...");
                await _pcSyncService.SyncNightlyUsageAsync();
                _logger.LogInformation($"[ZENITH] Extracción CSP NCE completada.");
            }
            catch (Exception ex)
            {
                // Aislamiento: Si Partner Center falla (ej. caída de API), lo capturamos y el motor sigue.
                _logger.LogError($"[ERROR CRÍTICO CSP] Fallo masivo en Partner Center: {ex.Message}");
            }

            // --- BLOQUE 2: BYOT ---
            try
            {
                _logger.LogInformation($"[ZENITH] Iniciando extracción externa BYOT...");
                await _directBillingService.SyncNightlyUsageAsync();
                _logger.LogInformation($"[ZENITH] Extracción externa BYOT completada.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ERROR CRÍTICO BYOT] Fallo masivo en Azure Direct: {ex.Message}");
            }

            _logger.LogInformation($"[ZENITH] Ciclo del motor de facturación finalizado.");
        }
    }
}