using Coem.Cmp.Core.Interfaces;
using Coem.Cmp.Infra.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Coem.Cmp.Web.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Route("Customer/[controller]")]
    [Authorize]
    public class ProductivityController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenantContext _tenantContext;

        public ProductivityController(ApplicationDbContext context, ITenantContext tenantContext)
        {
            _context = context;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 12)
        {
            // Seguridad: Bloqueo inmediato si no hay contexto de cliente
            if (!_tenantContext.CurrentTenantId.HasValue)
            {
                return Unauthorized("Acceso restringido a usuarios de portal de cliente.");
            }

            // 1. Contamos el total de registros (Filtrado automáticamente por TenantId en la DB)
            var totalRecords = await _context.Subscriptions
                .Where(s => s.IsSaaS)
                .CountAsync();

            // 2. Traemos solo la porción de datos necesaria
            var subscriptions = await _context.Subscriptions
                .Where(s => s.IsSaaS)
                .OrderBy(s => s.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // 3. Metadata para la navegación en la Vista
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            ViewBag.HasPreviousPage = page > 1;
            ViewBag.HasNextPage = page < ViewBag.TotalPages;

            return View(subscriptions);
        }

        [HttpGet("Details/{id}")]
        public async Task<IActionResult> Details(int id)
        {
            // El Global Query Filter asegura que un cliente no pueda ver el ID de otro
            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == id);

            if (subscription == null)
            {
                return NotFound();
            }

            return View(subscription);
        }
    }
}