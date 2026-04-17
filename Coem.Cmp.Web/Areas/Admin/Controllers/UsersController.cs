using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;
using Coem.Cmp.Web.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

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

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // 🛡️ ZENITH: Ignoramos filtros para que el Administrador vea el panorama completo
            var usersQuery = await _context.UserProfiles
                .IgnoreQueryFilters()
                .Include(u => u.Role)
                .Include(u => u.Tenant)
                .OrderBy(u => u.Upn)
                .ToListAsync();

            // Mapeo manual y seguro. Si Id es int, se pasa a int. Si es Guid, a Guid.
            // Según tu esquema, UserProfile.Id es int.
            var viewModels = usersQuery.Select(u => new UserListViewModel
            {
                Id = u.Id, // Asegúrate que en UserListViewModel 'Id' sea el mismo tipo que en UserProfile
                Email = u.Upn,
                DisplayName = u.DisplayName ?? "Pendiente",
                RoleName = u.Role?.Name ?? "Sin Rol",
                TenantName = u.Tenant?.Name ?? "Coem / Global",
                IsActive = u.IsActive,
                RoleId = u.RoleId,
                TenantId = u.TenantId ?? 0
            }).ToList();

            return View(viewModels);
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
            // 🛡️ CORRECCIÓN DE TIPOS: Si Role.Id es int, model.RoleId debe ser int.
            // Si Copilot te puso un ToString() aquí, arruinó el rendimiento de la consulta.
            var role = await _context.Roles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == model.RoleId);

            if (role != null)
            {
                // Purga de lógica regional y de cliente
                if (!role.Name.Contains("Comercial")) model.Country = null;
                if (!role.Name.Contains("Client")) model.TenantId = null;

                if (role.Name.Contains("Comercial") && string.IsNullOrEmpty(model.Country))
                    ModelState.AddModelError("Country", "Asigna un país para este perfil comercial.");

                if (role.Name.Contains("Client") && !model.TenantId.HasValue)
                    ModelState.AddModelError("TenantId", "Selecciona una organización para este cliente.");
            }

            if (ModelState.IsValid)
            {
                var cleanUpn = model.Upn.Trim().ToLower();

                // Validación de duplicados real (en toda la DB)
                bool exists = await _context.UserProfiles.IgnoreQueryFilters().AnyAsync(u => u.Upn == cleanUpn);
                if (exists)
                {
                    ModelState.AddModelError("Upn", "Esta identidad ya existe.");
                    await PopulateDropdownsAsync();
                    return View(model);
                }

                var displayName = cleanUpn.Split('@')[0];

                var newUser = new UserProfile
                {
                    Upn = cleanUpn,
                    DisplayName = displayName,
                    RoleId = model.RoleId,
                    Country = model.Country,
                    TenantId = (model.TenantId == 0) ? null : model.TenantId,
                    IsActive = true
                };

                _context.UserProfiles.Add(newUser);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Identidad {newUser.Upn} creada.";
                return RedirectToAction(nameof(Index));
            }

            await PopulateDropdownsAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id) // ⚠️ Cambiado a int para coincidir con UserProfile.Id
        {
            var user = await _context.UserProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Estado de {user.Upn} actualizado.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateDropdownsAsync()
        {
            // 🛡️ ZENITH: IgnoreQueryFilters() es la única forma de que el modal no salga vacío
            var roles = await _context.Roles
                .IgnoreQueryFilters()
                .OrderBy(r => r.Name)
                .ToListAsync();

            ViewBag.Roles = roles.Select(r => new SelectListItem
            {
                Value = r.Id.ToString(),
                Text = r.Name
            }).ToList();

            var tenants = await _context.Tenants
                .IgnoreQueryFilters()
                .OrderBy(t => t.Name)
                .ToListAsync();

            ViewBag.Tenants = new SelectList(tenants, "Id", "Name");

            ViewBag.Countries = new SelectList(new List<string> { "Colombia", "Ecuador", "Perú", "Panamá", "Regional" });
        }
    }
}