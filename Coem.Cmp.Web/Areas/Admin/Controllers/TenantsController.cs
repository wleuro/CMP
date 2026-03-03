using Coem.Cmp.Infra.Data;
using Coem.Cmp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;

namespace Coem.Cmp.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/Tenants")]
    public class TenantsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPartnerCenterSyncService _syncService;
        private readonly IDataProtector _protector;

        public TenantsController(ApplicationDbContext context, IPartnerCenterSyncService syncService, IDataProtectionProvider dataProtectionProvider)
        {
            _context = context;
            _syncService = syncService;
            _protector = dataProtectionProvider.CreateProtector("Coem.Cmp.RegionalSecrets.v1");
        }

        // =========================================================================
        // RADAR REGIONAL EVOLUCIONADO (Con Filtro por KPI de Consumo)
        // =========================================================================
        [HttpGet("")]
        public async Task<IActionResult> Index(string countryFilter, string searchQuery, string categoryFilter)
        {
            var availableCountries = await _context.PartnerCenterCredentials
                .Where(c => c.IsActive)
                .Select(c => c.CountryName)
                .Distinct()
                .ToListAsync();

            ViewBag.Countries = availableCountries;
            ViewBag.CurrentFilter = countryFilter;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.CategoryFilter = categoryFilter; // NUEVO: Rastreamos qué KPI clickeó el usuario

            var query = _context.Tenants.Include(t => t.Subscriptions).AsQueryable();

            // 1. Aplicamos los filtros básicos primero (País y Texto)
            if (!string.IsNullOrEmpty(countryFilter))
            {
                query = query.Where(t => t.Country == countryFilter);
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(t =>
                    t.Name.Contains(searchQuery) ||
                    t.DefaultDomain.Contains(searchQuery) ||
                    t.MicrosoftTenantId.ToString().Contains(searchQuery));
            }

            // 2. CÁLCULOS TÁCTICOS: Calculamos los números de los KPIs ANTES de filtrar por categoría
            // (Esto asegura que los cuadros mantengan sus totales aunque apliques el filtro)
            var allFiltered = await query.ToListAsync();
            ViewBag.TotalAP = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "AP"));
            ViewBag.TotalAL = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "AL") && !t.Subscriptions.Any(s => s.Category == "AP"));
            ViewBag.TotalColab = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "Colab") && !t.Subscriptions.Any(s => s.Category == "AP" || s.Category == "AL"));
            ViewBag.EnRevision = allFiltered.Count(t => !t.Subscriptions.Any());

            // 3. FILTRO POR KPI (El clic en los botones de colores)
            if (!string.IsNullOrEmpty(categoryFilter))
            {
                if (categoryFilter == "AP")
                    query = query.Where(t => t.Subscriptions.Any(s => s.Category == "AP"));
                else if (categoryFilter == "AL")
                    query = query.Where(t => t.Subscriptions.Any(s => s.Category == "AL") && !t.Subscriptions.Any(s => s.Category == "AP"));
                else if (categoryFilter == "Colab")
                    query = query.Where(t => t.Subscriptions.Any(s => s.Category == "Colab") && !t.Subscriptions.Any(s => s.Category == "AP" || s.Category == "AL"));
                else if (categoryFilter == "None")
                    query = query.Where(t => !t.Subscriptions.Any());
            }

            var tenants = await query.OrderByDescending(t => t.OnboardingDate).AsNoTracking().ToListAsync();

            return View(tenants);
        }

        [HttpPost("Sync")]
        public async Task<IActionResult> Sync()
        {
            try
            {
                int count = await _syncService.SyncCustomersAsync();
                TempData["SuccessMessage"] = $"Sincronización de identidades exitosa. {count} clientes procesados.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error sincronizando clientes: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("SyncSubscriptions")]
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

        // Script residual de prueba financiera (Opcional mantenerlo aquí ya que ahora vive mejor en CountriesController, pero lo dejamos para no romperte nada extra)
        [HttpGet("TestBillingAccess/{countryId}")]
        public async Task<IActionResult> TestBillingAccess(int countryId)
        {
            try
            {
                var cred = await _context.PartnerCenterCredentials.FindAsync(countryId);
                if (cred == null) return Content("ERROR: País no encontrado en la bóveda.", "text/plain");

                var plainTextSecret = _protector.Unprotect(cred.ClientSecret);

                var app = ConfidentialClientApplicationBuilder.Create(cred.ClientId)
                    .WithClientSecret(plainTextSecret)
                    .WithAuthority(new Uri($"https://login.microsoftonline.com/{cred.TenantId}"))
                    .Build();

                string[] scopes = new string[] { "https://api.partnercenter.microsoft.com/.default" };
                var authResult = await app.AcquireTokenForClient(scopes).ExecuteAsync();

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);

                var response = await client.GetAsync("https://api.partnercenter.microsoft.com/v1/invoices");

                if (response.IsSuccessStatusCode)
                {
                    return Content($"ÉXITO TÁCTICO en {cred.CountryName}: La App tiene el rol de Admin Agent.", "text/plain");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return Content($"ACCESO DENEGADO en {cred.CountryName} ({(int)response.StatusCode}).", "text/plain");
                }
            }
            catch (Exception ex)
            {
                return Content($"ERROR DE INFRAESTRUCTURA: {ex.Message}", "text/plain");
            }
        }
    }
}