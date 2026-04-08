using Coem.Cmp.Infra.Data;
using Microsoft.AspNetCore.Authentication; // VITAL: Aquí vive IClaimsTransformation
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Coem.Cmp.Web.Services
{
    // La herencia ": IClaimsTransformation" es el contrato que el compilador te estaba exigiendo
    public class ClaimsTransformation : IClaimsTransformation
    {
        private readonly ApplicationDbContext _context;

        public ClaimsTransformation(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var email = principal.Identity?.Name; // El UPN que viene de Entra ID
            var nameFromToken = principal.FindFirst("name")?.Value; // El nombre real en Microsoft

            if (!string.IsNullOrEmpty(email))
            {
                var profile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.Upn == email);

                // Sincronización JIT: Si existe en la bóveda pero no tiene nombre (o cambió)
                if (profile != null && profile.DisplayName != nameFromToken && !string.IsNullOrEmpty(nameFromToken))
                {
                    profile.DisplayName = nameFromToken;
                    await _context.SaveChangesAsync();
                }
            }

            return principal;
        }
    }
}