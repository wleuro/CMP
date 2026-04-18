using Coem.Cmp.Infra.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System;
using System.Linq;
using System.Threading.Tasks;
using Coem.Cmp.Core.Entities;

namespace Coem.Cmp.Web.Services
{
    public class ClaimsTransformation : IClaimsTransformation
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<ClaimsTransformation> _logger;

        public ClaimsTransformation(
            ApplicationDbContext context,
            IConfiguration config,
            ILogger<ClaimsTransformation> logger)
        {
            _context = context;
            _config = config;
            _logger = logger;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity == null || !principal.Identity.IsAuthenticated)
            {
                return principal;
            }

            // Clonamos para evitar efectos colaterales en el middleware
            var clone = principal.Clone();
            var identity = (ClaimsIdentity)clone.Identity!;

            // 1. Extracción de Identidad (UPN)
            // Microsoft Entra ID suele enviar el correo en 'preferred_username' o 'name'
            var upn = principal.FindFirst("preferred_username")?.Value
                      ?? principal.FindFirst(ClaimTypes.Name)?.Value
                      ?? principal.Identity.Name;

            if (string.IsNullOrEmpty(upn))
            {
                _logger.LogWarning("No se pudo extraer el UPN del token de identidad.");
                return principal;
            }

            // 2. Sincronización con la Verdad (Base de Datos Nuclear)
            // CRÍTICO: Usamos .IgnoreQueryFilters() porque si no, el filtro Multi-tenant 
            // nos oculta el perfil del usuario antes de saber quién es.
            var profile = await _context.UserProfiles
                .Include(u => u.Role)
                .Include(u => u.Tenant)
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Upn.ToLower() == upn.ToLower());

            if (profile != null && profile.IsActive)
            {
                // Inyectamos el Rol con el esquema estándar de Microsoft
                if (profile.Role != null)
                {
                    var roleName = profile.Role.Name.Replace(" ", ""); // GlobalAdmin

                    // Sello de Autoridad oficial
                    identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
                    identity.AddClaim(new Claim("CmpRole", roleName));

                    // 3. Definición de Alcance (Scope)
                    // Si es Admin Global, le damos las llaves del reino
                    if (roleName == "GlobalAdmin")
                    {
                        identity.AddClaim(new Claim("CmpScope", "Global"));
                        // Un Admin Global no está atado a un solo Tenant para las consultas
                    }
                    else if (profile.TenantId.HasValue)
                    {
                        identity.AddClaim(new Claim("CmpTenantId", profile.TenantId.Value.ToString()));
                        identity.AddClaim(new Claim("CmpScope", "SingleTenant"));
                    }
                }

                // 4. Metadata de Usuario
                if (!identity.HasClaim(c => c.Type == "CmpCountry"))
                {
                    var country = profile.Country ?? profile.Tenant?.Country ?? "Colombia";
                    identity.AddClaim(new Claim("CmpCountry", country));
                }
            }
            else
            {
                _logger.LogWarning("Usuario {Upn} autenticado pero no encontrado en UserProfiles.", upn);
                identity.AddClaim(new Claim("CmpScope", "Guest"));
            }

            return clone;
        }
    }
}