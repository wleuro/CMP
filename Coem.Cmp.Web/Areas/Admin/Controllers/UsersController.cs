using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;
using Coem.Cmp.Web.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Coem.Cmp.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "TenantSetup")]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public UsersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. EL LISTADO MAESTRO (Sin esto el RedirectToAction fallaba)
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var users = await _context.UserProfiles
                .Include(u => u.Role)
                .Include(u => u.Tenant)
                .OrderBy(u => u.Upn)
                .ToListAsync();

            return View(users);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateDropdownsAsync();
            return View(new UserCreateViewModel { Upn = string.Empty });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserCreateViewModel model)
        {
            var role = await _context.Roles.FindAsync(model.RoleId);

            // Lógica de purga de seguridad: El backend no confía en el frontend.
            if (role != null)
            {
                if (role.Name != "Comercial") model.Country = null;

                bool isClientRole = role.Name.StartsWith("Client");
                if (!isClientRole) model.TenantId = null;

                // Validaciones condicionales estrictas
                if (role.Name == "Comercial" && string.IsNullOrEmpty(model.Country))
                    ModelState.AddModelError("Country", "Un Comercial requiere un país asignado.");

                if (isClientRole && !model.TenantId.HasValue)
                    ModelState.AddModelError("TenantId", "Un perfil de cliente requiere un Tenant asignado.");
            }

            if (ModelState.IsValid)
            {
                var cleanUpn = model.Upn.Trim().ToLower();

                // 2. VALIDACIÓN DE DUPLICADOS (Evita colapso de Entity Framework)
                bool exists = await _context.UserProfiles.AnyAsync(u => u.Upn == cleanUpn);
                if (exists)
                {
                    ModelState.AddModelError("Upn", "Esta identidad ya se encuentra provisionada en el sistema.");
                    await PopulateDropdownsAsync();
                    return View(model);
                }

                // Extraer DisplayName del email (parte antes del @)
                var displayName = cleanUpn.Contains("@") ? cleanUpn.Split("@")[0] : cleanUpn;

                var newUser = new UserProfile
                {
                    Upn = cleanUpn,
                    DisplayName = displayName,
                    RoleId = model.RoleId,
                    Country = model.Country,
                    TenantId = model.TenantId,
                    IsActive = true
                };

                _context.UserProfiles.Add(newUser);
                await _context.SaveChangesAsync();

                // Mensaje limpio para la operación del equipo (Sin jergas)
                TempData["Success"] = $"Identidad {newUser.Upn} provisionada exitosamente.";
                return RedirectToAction(nameof(Index));
            }

            await PopulateDropdownsAsync();
            return View(model);
        }

        // 3. SOFT DELETE (Bloquear acceso sin destruir la historia financiera)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var user = await _context.UserProfiles.FindAsync(id);
            if (user == null) return NotFound();

            // Si es tu propio usuario, podrías poner una validación extra aquí para no auto-bloquearte
            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Estado de acceso para {user.Upn} actualizado.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateDropdownsAsync()
        {
            var roles = await _context.Roles.OrderBy(r => r.Name).ToListAsync();
            ViewBag.Roles = roles.Select(r => new SelectListItem
            {
                Value = r.Id.ToString(),
                Text = r.Name
            }).ToList();

            ViewBag.Tenants = new SelectList(await _context.Tenants.OrderBy(t => t.Name).ToListAsync(), "Id", "Name");

            // Si en el futuro tienes una tabla de Territorios, sácalo de ahí. Por ahora estático.
            ViewBag.Countries = new SelectList(new List<string> { "Colombia", "Ecuador", "Perú", "Panamá", "Regional" });
        }
    }
}