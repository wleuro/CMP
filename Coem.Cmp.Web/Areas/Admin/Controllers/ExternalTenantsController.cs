using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;
using System.Text.Json;

namespace Coem.Cmp.Web.Controllers.Admin
{
    [Area("Admin")]
    [Route("Admin/ExternalTenants")]
    public class ExternalTenantsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IDataProtector _protector;
        private readonly IHttpClientFactory _httpClientFactory;

        public ExternalTenantsController(
            ApplicationDbContext context,
            IDataProtectionProvider dataProtectionProvider,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _protector = dataProtectionProvider.CreateProtector("Coem.Cmp.RegionalSecrets.v1");
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var connectors = await _context.AzureDirectCredentials.ToListAsync();

            // Traemos las suscripciones de la tabla AISLADA (ExternalSubscriptions)
            var externalSubs = await _context.ExternalSubscriptions.ToListAsync();

            // Agrupamos por Credencial para que la vista las encuentre por ID de conector
            ViewBag.SyncedSubscriptions = externalSubs
                .GroupBy(s => s.AzureDirectCredentialId)
                .ToDictionary(g => g.Key.ToString(), g => g.ToList());

            return View(connectors);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string alias, string tenantId, string clientId, string plainSecret)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(plainSecret))
            {
                TempData["ErrorMessage"] = "Todos los campos son obligatorios.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var newCredential = new AzureDirectCredential
                {
                    Alias = alias,
                    TenantId = tenantId.Trim(),
                    ClientId = clientId.Trim(),
                    ClientSecret = _protector.Protect(plainSecret),
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };

                _context.AzureDirectCredentials.Add(newCredential);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Conector '{alias}' registrado con éxito.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al registrar: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Edit/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string alias, string tenantId, string clientId, string plainSecret)
        {
            var cred = await _context.AzureDirectCredentials.FindAsync(id);
            if (cred == null) return NotFound();

            cred.Alias = alias;
            cred.TenantId = tenantId.Trim();
            cred.ClientId = clientId.Trim();

            if (!string.IsNullOrWhiteSpace(plainSecret))
            {
                cred.ClientSecret = _protector.Protect(plainSecret);
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Conector actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Delete/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var credential = await _context.AzureDirectCredentials.FindAsync(id);
            if (credential != null)
            {
                _context.AzureDirectCredentials.Remove(credential);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Conector y llaves eliminadas de la bóveda.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("RemoveSubscription/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveSubscription(Guid id)
        {
            // Apuntamos a la tabla externa para no tocar CSP
            var sub = await _context.ExternalSubscriptions.FindAsync(id);
            if (sub != null)
            {
                _context.ExternalSubscriptions.Remove(sub);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Suscripción removida del radar BYOT.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("TestConnection/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestConnection(int id)
        {
            var credential = await _context.AzureDirectCredentials.FindAsync(id);
            if (credential == null) return RedirectToAction(nameof(Index));

            try
            {
                var plainTextSecret = _protector.Unprotect(credential.ClientSecret);
                var app = Microsoft.Identity.Client.ConfidentialClientApplicationBuilder.Create(credential.ClientId)
                    .WithClientSecret(plainTextSecret)
                    .WithAuthority(new Uri($"https://login.microsoftonline.com/{credential.TenantId}"))
                    .Build();

                string[] scopes = new string[] { "https://management.azure.com/.default" };
                var authResult = await app.AcquireTokenForClient(scopes).ExecuteAsync();

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResult.AccessToken);

                var response = await client.GetAsync("https://management.azure.com/subscriptions?api-version=2022-12-01");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var armData = JsonDocument.Parse(content);

                    if (armData.RootElement.TryGetProperty("value", out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            var subIdStr = item.GetProperty("subscriptionId").GetString();
                            var subId = Guid.Parse(subIdStr);
                            var subName = item.GetProperty("displayName").GetString();

                            // --- PRUEBA ÁCIDA DE ROLES ---
                            var costPing = await client.GetAsync($"https://management.azure.com/subscriptions/{subIdStr}/providers/Microsoft.Consumption/usageDetails?$top=1&api-version=2023-11-01");
                            var readPing = await client.GetAsync($"https://management.azure.com/subscriptions/{subIdStr}/resources?$top=1&api-version=2021-04-01");

                            var auditResult = $"Cost:{(costPing.IsSuccessStatusCode ? "OK" : "FAIL")}|Read:{(readPing.IsSuccessStatusCode ? "OK" : "FAIL")}";

                            // GUARDADO O ACTUALIZACIÓN EN TABLA EXTERNA
                            var existingSub = await _context.ExternalSubscriptions.FirstOrDefaultAsync(s => s.Id == subId);
                            if (existingSub == null)
                            {
                                _context.ExternalSubscriptions.Add(new ExternalSubscription
                                {
                                    Id = subId,
                                    AzureDirectCredentialId = credential.Id,
                                    Name = subName,
                                    Status = item.GetProperty("state").GetString(),
                                    AuditResult = auditResult,
                                    LastSync = DateTime.UtcNow
                                });
                            }
                            else
                            {
                                existingSub.AuditResult = auditResult;
                                existingSub.Name = subName;
                                existingSub.Status = item.GetProperty("state").GetString();
                                existingSub.LastSync = DateTime.UtcNow;
                            }
                        }
                        await _context.SaveChangesAsync();
                        TempData["SuccessMessage"] = $"Suscripciones externas auditadas y guardadas. Revisa la lupa.";
                    }
                }
                else
                {
                    TempData["ErrorMessage"] = $"Acceso denegado a ARM: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error técnico: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}