using Coem.Cmp.Infra.Data;
using Coem.Cmp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using System.Net.Http.Headers;

namespace Coem.Cmp.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/[controller]/[action]")]
    // El filtro de autorización global ya protege esto, pero la semántica es clave.
    public class TenantsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPartnerCenterSyncService _syncService;

        public TenantsController(ApplicationDbContext context, IPartnerCenterSyncService syncService)
        {
            _context = context;
            _syncService = syncService;
        }

        public async Task<IActionResult> Index()
        {
            // AsNoTracking porque solo vamos a leer datos, no a modificarlos aquí.
            // EL CAMBIO ESTRATÉGICO: Incluimos las suscripciones para que la vista sepa quién es de Azure.
            var tenants = await _context.Tenants
                .Include(t => t.Subscriptions)
                .AsNoTracking()
                .ToListAsync();

            return View(tenants);
        }

        [HttpPost]
        public async Task<IActionResult> Sync()
        {
            try
            {
                int count = await _syncService.SyncCustomersAsync();
                TempData["SuccessMessage"] = $"Sincronización de identidades exitosa. {count} clientes procesados.";
            }
            catch (Exception ex)
            {
                // Un verdadero CMP maneja sus errores, no explota en la cara del usuario.
                TempData["ErrorMessage"] = $"Error sincronizando clientes: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================================
        // NUEVO ENDPOINT: El gatillo del Radar de Suscripciones
        // =========================================================================
        [HttpPost]
        public async Task<IActionResult> SyncSubscriptions()
        {
            try
            {
                int count = await _syncService.SyncSubscriptionsAsync();
                TempData["SuccessMessage"] = $"Auditoría de Suscripciones completada. {count} suscripciones evaluadas.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error auditando suscripciones: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
        // =========================================================================
        // SCRIPT DE RECONOCIMIENTO: Prueba de Fuego Financiera
        // =========================================================================
        [HttpGet]
        public async Task<IActionResult> TestBillingAccess([FromServices] IConfiguration config)
        {
            try
            {
                var tenantId = config["PartnerCenter:TenantId"];
                var clientId = config["PartnerCenter:ClientId"];
                var clientSecret = config["PartnerCenter:ClientSecret"];

                var app = ConfidentialClientApplicationBuilder.Create(clientId)
                    .WithClientSecret(clientSecret)
                    .WithAuthority(new Uri($"https://login.microsoftonline.com/{tenantId}"))
                    .Build();

                string[] scopes = new string[] { "https://api.partnercenter.microsoft.com/.default" };
                var authResult = await app.AcquireTokenForClient(scopes).ExecuteAsync();

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);

                // El endpoint que separa a los administradores de los espectadores
                var response = await client.GetAsync("https://api.partnercenter.microsoft.com/v1/invoices");

                if (response.IsSuccessStatusCode)
                {
                    return Content("ÉXITO TÁCTICO: La App tiene el rol de Admin Agent. Tienes las llaves de la bóveda financiera.", "text/plain");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return Content($"ACCESO DENEGADO ({(int)response.StatusCode}): Sebastián no asignó los permisos. La App está ciega.\nDetalle de Microsoft: {error}", "text/plain");
                }
            }
            catch (Exception ex)
            {
                return Content($"ERROR DE INFRAESTRUCTURA: {ex.Message}", "text/plain");
            }
        }


    }
}