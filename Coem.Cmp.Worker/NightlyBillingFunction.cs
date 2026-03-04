//using System;
//using System.Threading.Tasks;
//using Microsoft.Azure.Functions.Worker;
//using Microsoft.Extensions.Logging;
//using Coem.Cmp.Web.Services;

//namespace Coem.Cmp.Worker
//{
//    public class NightlyBillingFunction
//    {
//        private readonly ILogger _logger;
//        private readonly IAzureDirectBillingService _billingService;

//        public NightlyBillingFunction(ILoggerFactory loggerFactory, IAzureDirectBillingService billingService)
//        {
//            _logger = loggerFactory.CreateLogger<NightlyBillingFunction>();
//            _billingService = billingService;
//        }

//        // Cron: A la 1:00 AM todos los días.
//        [Function("SyncExternalUsageNightly")]
//        public async Task Run([TimerTrigger("0 0 1 * * *")] TimerInfo myTimer)
//        {
//            _logger.LogInformation($"[ZENITH] Worker BYOT disparado a las: {DateTime.Now}");

//            try
//            {
//                // Disparamos el motor financiero de la capa Web
//                await _billingService.SyncNightlyUsageAsync();

//                _logger.LogInformation($"[ZENITH] Carga de consumos externos completada con éxito.");
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError($"[FATAL ERROR] Fallo en el motor nocturno BYOT: {ex.Message}");
//            }
//        }
//    }
//}