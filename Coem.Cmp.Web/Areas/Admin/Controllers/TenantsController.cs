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
        // RADAR REGIONAL CON FILTROS FINOPS
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

            // 1. Filtros principales (País y Texto)
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

            // 2. CÁLCULOS DE KPI (Conteo de clientes que poseen al menos un servicio de la categoría)
            var allFiltered = await query.ToListAsync();

            ViewBag.TotalAP = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "AP"));
            ViewBag.TotalM365 = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "M365"));
            ViewBag.TotalInfra = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "INFRA"));
            ViewBag.TotalDev = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "DEV"));
            ViewBag.TotalAL = allFiltered.Count(t => t.Subscriptions.Any(s => s.Category == "AL"));
            ViewBag.EnRevision = allFiltered.Count(t => !t.Subscriptions.Any());

            // 3. FILTRO DE CATEGORÍA (Intersección limpia, sin exclusiones mutuas)
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

        // Endpoint de diagnóstico de permisos Graph/Partner Center
        [HttpGet("TestBillingAccess/{countryId}")]
        public async Task<IActionResult> TestBillingAccess(int countryId)
        {
            try
            {
                var cred = await _context.PartnerCenterCredentials.FindAsync(countryId);
                if (cred == null) return Content("ERROR: País no encontrado en la base de datos.", "text/plain");

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
                    return Content($"AUTORIZADO en {cred.CountryName}: La aplicación tiene permisos correctos de lectura.", "text/plain");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return Content($"ACCESO DENEGADO en {cred.CountryName} ({(int)response.StatusCode}). Detalle: {error}", "text/plain");
                }
            }
            catch (Exception ex)
            {
                return Content($"ERROR DE EJECUCIÓN: {ex.Message}", "text/plain");
            }
        }
    }
}