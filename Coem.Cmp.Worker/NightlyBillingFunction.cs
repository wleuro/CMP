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
            // TÁCTICA DE BYPASS: Cambia a 'true' solo cuando quieras sincronizar TODO (Tenants, SaaS, etc.)
            // Para tus pruebas actuales de consumo, mantenlo en 'false'.
            bool ejecutarCicloCompleto = true;

            _logger.LogInformation($"[ORQUESTADOR COEM] Modo {(ejecutarCicloCompleto ? "FULL" : "SÓLO CONSUMO")}. Inicio: {DateTime.UtcNow} UTC");

            if (ejecutarCicloCompleto)
            {
                // FASE 1: INFRAESTRUCTURA DE CLIENTES
                try
                {
                    _logger.LogInformation("Fase 1: Sincronizando metadatos de clientes CSP...");
                    await _pcSyncService.SyncCustomersAsync();
                }
                catch (Exception ex) { _logger.LogError($"[FALLO FASE 1] {ex.Message}"); }

                // FASE 1.5: MÓDULO SaaS
                try
                {
                    _logger.LogInformation("Fase 1.5: Extrayendo inventario de licencias SaaS...");
                    await _pcSyncService.SyncSaaSSubscriptionsAsync();
                }
                catch (Exception ex) { _logger.LogError($"[FALLO FASE 1.5] {ex.Message}"); }

                // FASE 2: SUSCRIPCIONES Y NCE
                try
                {
                    _logger.LogInformation("Fase 2: Sincronizando suscripciones y facturación oficial...");
                    await _pcSyncService.SyncSubscriptionsAsync();
                    await _pcSyncService.SyncNightlyUsageAsync("current");
                }
                catch (Exception ex) { _logger.LogError($"[FALLO FASE 2] {ex.Message}"); }
            }

            // ==========================================================
            // FASE 2.5: CONSUMO OPERATIVO CSP (OBJETIVO ACTUAL)
            // ==========================================================
            try
            {
                _logger.LogInformation("Fase 2.5: Extrayendo consumo operativo (Torniquete CSP)...");
                await _pcSyncService.SyncCspOperationalConsumptionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FALLO CRÍTICO CONSUMO] Error en radar operativo: {ex.Message}");
            }

            if (ejecutarCicloCompleto)
            {
                // FASE 3 y 4: ENTORNOS EXTERNOS (DIRECT)
                try
                {
                    _logger.LogInformation("Fase 3: Sincronizando metadatos directos...");
                    await _azureDirectService.SyncDirectSubscriptionsAsync();
                    _logger.LogInformation("Fase 4: Extrayendo consumo operativo externo...");
                    await _azureDirectService.SyncDailyConsumptionAsync();
                }
                catch (Exception ex) { _logger.LogError($"[FALLO FASE 3/4] {ex.Message}"); }
            }

            _logger.LogInformation("[ORQUESTADOR COEM] Ciclo finalizado.");
        }
    }
}