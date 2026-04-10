using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker; // ¡Asegúrate de que sea .Worker, NO .WebJobs!
using Microsoft.Extensions.Logging;
using Coem.Cmp.Web.Services;

namespace Coem.Cmp.Worker
{
    public class NightlyBillingFunction
    {
        private readonly ILogger<NightlyBillingFunction> _logger;
        private readonly IPartnerCenterSyncService _pcSyncService;

        public NightlyBillingFunction(ILogger<NightlyBillingFunction> logger, IPartnerCenterSyncService pcSyncService)
        {
            _logger = logger;
            _pcSyncService = pcSyncService;
        }

        [Function("SyncNightlyUsage")] // ¡Etiqueta [Function], NO [FunctionName]!
        public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation($"[ZENITH] Motor FinOps despertando a las: {DateTime.UtcNow} UTC");
            await _pcSyncService.SyncNightlyUsageAsync();
            _logger.LogInformation($"[ZENITH] Ciclo completado.");
        }
    }
}