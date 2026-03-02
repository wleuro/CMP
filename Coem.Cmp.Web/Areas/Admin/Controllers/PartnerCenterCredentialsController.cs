using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // Para ToListAsync
using Coem.Cmp.Core.Entities;      // Para reconocer PartnerCenterCredential
using Coem.Cmp.Infra.Data;         // Para reconocer tu ApplicationDbContext

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
public class PartnerCenterCredentialsController : Controller
{
    private readonly ApplicationDbContext _context;

    public PartnerCenterCredentialsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        // Lista las credenciales de Colombia, Perú, etc.
        var credentials = await _context.Set<PartnerCenterCredential>().ToListAsync();
        return View(credentials);
    }

    [HttpPost]
    public async Task<IActionResult> Create(PartnerCenterCredential model)
    {
        // Aquí guardarás las llaves que te entreguen de los otros países
        _context.Add(model);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}