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
        // RADAR REGIONAL ZENITH CON FILTROS ESTRATÉGICOS
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
            ViewBag.CategoryFilter = categoryFilter;

            var query = _context.Tenants.Include(t => t.Subscriptions).AsQueryable();

            // 1. FILTROS DE RADAR (GEOGRÁFICO Y BÚSQUEDA)
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

            // 2. CÁLCULOS DE KPI - VISIBILIDAD DE 12 UNIDADES DE NEGOCIO
            var allFiltered = await query.ToListAsync();

            ViewBag.TotalAP = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "AP"));
            ViewBag.TotalM365 = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "M365"));
            ViewBag.TotalAI = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "AI"));
            ViewBag.TotalSeguridad = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "SEGURIDAD"));
            ViewBag.TotalPowerPlatform = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "POWERPLATFORM"));
            ViewBag.TotalTelefonia = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "TELEFONIA"));
            ViewBag.TotalDynamics = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "DYNAMICS"));
            ViewBag.TotalBI = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "BI"));

            ViewBag.TotalInfra = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "INFRA"));
            ViewBag.TotalDev = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "DEV"));
            ViewBag.TotalAL = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "AL"));
            ViewBag.EnRevision = allFiltered.Count(t => !t.Subscriptions.Any());

            // 3. APLICACIÓN DEL FILTRO DE CATEGORÍA ACTIVO
            if (!string.IsNullOrEmpty(categoryFilter))
            {
                if (categoryFilter == "None")
                {
                    query = query.Where(t => !t.Subscriptions.Any());
                }
                else
                {
                    query = query.Where(t => t.Subscriptions.Any(s => s.Category == categoryFilter));
                }
            }

            var tenants = await query.OrderByDescending(t => t.OnboardingDate).AsNoTracking().ToListAsync();

            return View(tenants);
        }

        // ACCIÓN 1: Sincronización de Identidades (Tenants)
        [HttpPost("Sync")]
        [ValidateAntiForgeryToken]
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

        // ACCIÓN 2: Auditoría y Reclasificación Total de Servicios
        [HttpPost("SyncSubscriptions")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncSubscriptions()
        {
            try
            {
                // 🚀 CLAVE ZENITH: Ejecutamos ambos para reclasificar TODO
                // 1. Sincronizamos licencias SaaS (M365, Dynamics, BI, Seguridad, AI)
                int saasCount = await _syncService.SyncSaaSSubscriptionsAsync();

                // 2. Sincronizamos suscripciones de Azure (Azure Plan, Infra, Dev)
                int azureCount = await _syncService.SyncSubscriptionsAsync();

                TempData["SuccessMessage"] = $"Auditoría completa: {saasCount} licencias y {azureCount} suscripciones de Azure procesadas.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error en la auditoría técnica: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        // Endpoint de diagnóstico de permisos
        [HttpGet("TestBillingAccess/{countryId}")]
        public async Task<IActionResult> TestBillingAccess(int countryId)
        {
            try
            {
                var cred = await _context.PartnerCenterCredentials.FindAsync(countryId);
                if (cred == null) return Content("ERROR: País no encontrado.");

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
                    return Content($"AUTORIZADO en {cred.CountryName}: Acceso correcto.");

                var error = await response.Content.ReadAsStringAsync();
                return Content($"DENEGADO en {cred.CountryName}: {error}");
            }
            catch (Exception ex)
            {
                return Content($"ERROR: {ex.Message}");
            }
        }
    }
}