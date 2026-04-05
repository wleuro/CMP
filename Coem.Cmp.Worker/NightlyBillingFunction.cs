using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Coem.Cmp.Web.Services;

namespace Coem.Cmp.Worker
{
    public class NightlyBillingFunction
    {
        private readonly ILogger _logger;
        private readonly IAzureDirectBillingService _directBillingService;
        private readonly IPartnerCenterSyncService _pcSyncService;

        public NightlyBillingFunction(ILoggerFactory loggerFactory,
                                      IAzureDirectBillingService directBillingService,
                                      IPartnerCenterSyncService pcSyncService)
        {
            _logger = loggerFactory.CreateLogger<NightlyBillingFunction>();
            _directBillingService = directBillingService;
            _pcSyncService = pcSyncService;
        }

        // CRON AJUSTADO A 10 SEGUNDOS PARA LA PRUEBA LOCAL
        [Function("SyncNightlyUsage")]
        public async Task Run([TimerTrigger("*/10 * * * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation($"[ZENITH] Motor FinOps despertando a las: {DateTime.Now}");

            try
            {
                _logger.LogInformation($"[ZENITH] Iniciando extracción nativa CSP...");
                await _pcSyncService.SyncNightlyUsageAsync();

                _logger.LogInformation($"[ZENITH] Iniciando extracción externa BYOT...");
                await _directBillingService.SyncNightlyUsageAsync();

                _logger.LogInformation($"[ZENITH] Operación completada exitosamente.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FATAL ERROR] Fallo masivo en el motor: {ex.Message}");
            }
        }
    }
}