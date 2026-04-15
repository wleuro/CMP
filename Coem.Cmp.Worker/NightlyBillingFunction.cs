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

        public NightlyBillingFunction(ILogger<NightlyBillingFunction> logger, IPartnerCenterSyncService pcSyncService)
        {
            _logger = logger;
            _pcSyncService = pcSyncService;
        }

        [Function("SyncNightlyUsage")]
        public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation($"Iniciando motor de facturación automatizado a las: {DateTime.UtcNow} UTC");

            try
            {
                //// Fase 1: Asegurar que todos los inquilinos nuevos existan en la BD
                //_logger.LogInformation("Fase 1: Sincronizando clientes...");
                //await _pcSyncService.SyncCustomersAsync();

                //// Fase 2: Asegurar que todas las suscripciones nuevas existan y estén categorizadasdotnetdo
                //_logger.LogInformation("Fase 2: Sincronizando suscripciones y aplicando reglas FinOps...");
                //await _pcSyncService.SyncSubscriptionsAsync();

                // Fase 3: Extracción segura de dólares
                _logger.LogInformation("Fase 3: Extrayendo facturación NCE del mes en curso...");
                await _pcSyncService.SyncNightlyUsageAsync("202602");

                _logger.LogInformation("Ciclo de facturación nocturno completado con éxito.");
            }
            catch (Exception ex)
            {
                // Es crítico propagar el error para que la infraestructura marque la ejecución como Fallida
                _logger.LogError($"Fallo crítico en la ejecución del worker nocturno: {ex.Message}");
                throw;
            }
        }
    }
}