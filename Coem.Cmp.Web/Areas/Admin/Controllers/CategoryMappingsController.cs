using Coem.Cmp.Core.Entities;
using Coem.Cmp.Infra.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace Coem.Cmp.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/CategoryMappings")]
    public class CategoryMappingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CategoryMappingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            ViewBag.Categories = await _context.CategoryDefinitions
                .OrderBy(c => c.Name)
                .ToListAsync();

            var rules = await _context.CategoryMappings
                .OrderBy(c => c.Priority)
                .ToListAsync();

            return View(rules);
        }

        // --- GESTIÓN DE REGLAS (MAPPINGS) ---

        [HttpGet("GetMapping/{id}")]
        public async Task<IActionResult> GetMapping(int id)
        {
            var mapping = await _context.CategoryMappings.FindAsync(id);
            if (mapping == null) return NotFound();
            return Json(mapping);
        }

        [HttpPost("Upsert")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upsert(CategoryMapping mapping)
        {
            if (!ModelState.IsValid) return RedirectToAction(nameof(Index));

            mapping.Keyword = mapping.Keyword.ToUpperInvariant().Trim();
            mapping.CategoryCode = mapping.CategoryCode.ToUpperInvariant().Trim();

            if (mapping.Id == 0) _context.Add(mapping);
            else _context.Update(mapping);

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Regla de clasificación procesada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        // --- GESTIÓN DE DEFINICIONES DE CATEGORÍA ---

        [HttpGet("GetCategoryDefinition/{id}")]
        public async Task<IActionResult> GetCategoryDefinition(int id)
        {
            var definition = await _context.CategoryDefinitions.FindAsync(id);
            if (definition == null) return NotFound();
            return Json(definition);
        }

        [HttpPost("UpsertDefinition")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpsertDefinition(CategoryDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.Code) || string.IsNullOrWhiteSpace(definition.Name))
            {
                TempData["ErrorMessage"] = "Código y Nombre son obligatorios.";
                return RedirectToAction(nameof(Index));
            }

            definition.Code = definition.Code.ToUpperInvariant().Trim();
            definition.Name = definition.Name.Trim();

            if (definition.Id == 0)
            {
                _context.CategoryDefinitions.Add(definition);
                TempData["SuccessMessage"] = $"Categoría '{definition.Name}' creada.";
            }
            else
            {
                _context.CategoryDefinitions.Update(definition);
                TempData["SuccessMessage"] = $"Categoría '{definition.Name}' actualizada.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // --- OPERACIONES DE ESTADO Y BORRADO ---

        [HttpPost("ToggleStatus/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var mapping = await _context.CategoryMappings.FindAsync(id);
            if (mapping == null) return Json(new { success = false });

            mapping.IsActive = !mapping.IsActive;
            await _context.SaveChangesAsync();

            return Json(new { success = true, isActive = mapping.IsActive });
        }

        // 🛡️ ZENITH: Quitamos el /{id} para que acepte el ID desde el FormBody del Modal
        [HttpPost("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var mapping = await _context.CategoryMappings.FindAsync(id);
            if (mapping != null)
            {
                _context.CategoryMappings.Remove(mapping);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Regla eliminada.";
            }
            return RedirectToAction(nameof(Index));
        }

        // 🛡️ ZENITH: Quitamos el /{id} para evitar el error 404 al usar modales
        [HttpPost("DeleteDefinition")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDefinition(int id)
        {
            var definition = await _context.CategoryDefinitions.FindAsync(id);
            if (definition != null)
            {
                _context.CategoryDefinitions.Remove(definition);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Definición de categoría eliminada.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}