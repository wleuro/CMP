using Coem.Cmp.Infra.Data;
using Coem.Cmp.Core.Entities;
using Coem.Cmp.Web.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        private readonly IConfiguration _config;

        public UsersController(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        private async Task<int?> GetCoemTenantIdAsync()
        {
            var coemMsId = _config["CmpSettings:CoemTenantId"];
            if (Guid.TryParse(coemMsId, out var coemGuid))
            {
                var tenant = await _context.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.MicrosoftTenantId == coemGuid);
                return tenant?.Id;
            }
            return null;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var coemId = await GetCoemTenantIdAsync();

            // 🛡️ ACCESO RADAR: Solo personal vinculado a COEM o staff raíz
            var usersQuery = await _context.UserProfiles
                .IgnoreQueryFilters()
                .Include(u => u.Role)
                .Include(u => u.Tenant)
                .Where(u => u.TenantId == coemId || u.TenantId == null)
                .OrderBy(u => u.Upn)
                .ToListAsync();

            var viewModels = usersQuery.Select(u => new UserListViewModel
            {
                Id = u.Id,
                Email = u.Upn,
                DisplayName = u.DisplayName ?? "Pendiente",
                RoleName = u.Role?.Name ?? "Sin Rol",
                TenantName = u.Tenant?.Name ?? "Staff Corporativo",
                IsActive = u.IsActive,
                RoleId = u.RoleId,
                TenantId = u.TenantId ?? 0
            }).ToList();

            await PopulateDropdownsAsync(); // VITAL para los modales en la misma vista
            return View(viewModels);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserCreateViewModel model)
        {
            var coemId = await GetCoemTenantIdAsync();

            if (ModelState.IsValid)
            {
                var cleanUpn = model.Upn.Trim().ToLower();

                bool exists = await _context.UserProfiles.IgnoreQueryFilters().AnyAsync(u => u.Upn == cleanUpn);
                if (exists)
                {
                    ModelState.AddModelError("Upn", "Esta identidad ya está registrada.");
                    return RedirectToAction(nameof(Index)); // Evitamos errores de estado en modales
                }

                var newUser = new UserProfile
                {
                    Upn = cleanUpn,
                    DisplayName = cleanUpn.Split('@')[0],
                    RoleId = model.RoleId,
                    Country = model.Country,
                    TenantId = coemId, // Inyección automática de COEM
                    IsActive = true
                };

                _context.UserProfiles.Add(newUser);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Identidad {newUser.Upn} autorizada.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var user = await _context.UserProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // 🛡️ ACCIÓN DE ELIMINACIÓN: Para limpiar la bóveda
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.UserProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            _context.UserProfiles.Remove(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Identidad purgada del sistema.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateDropdownsAsync()
        {
            // Solo roles corporativos
            var roles = await _context.Roles
                .IgnoreQueryFilters()
                .Where(r => !r.Name.Contains("Client"))
                .OrderBy(r => r.Name)
                .ToListAsync();

            ViewBag.Roles = roles.Select(r => new SelectListItem
            {
                Value = r.Id.ToString(),
                Text = r.Name
            }).ToList();

            ViewBag.Countries = new List<string> { "Colombia", "Ecuador", "Perú", "Panamá", "Regional" }
                .Select(c => new SelectListItem { Value = c, Text = c }).ToList();
        }
    }
}