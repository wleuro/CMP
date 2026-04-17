using Coem.Cmp.Infra.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System;
using System.Threading.Tasks;

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
            // 1. Verificación de seguridad base y autenticación
            if (!(principal.Identity is ClaimsIdentity identity) || !identity.IsAuthenticated)
            {
                return principal;
            }

            // 2. Clonamos el Principal para inyectar claims de forma segura (Inmutabilidad)
            var clone = principal.Clone();
            var newIdentity = (ClaimsIdentity)clone.Identity!;

            var email = principal.Identity.Name; // UPN
            var nameFromToken = principal.FindFirst("name")?.Value;

            // Extraer el TenantId del token de Entra ID (claim estándar 'tid' o el esquema completo)
            var msTenantIdClaim = principal.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
                                 ?? principal.FindFirst("tid")?.Value;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(msTenantIdClaim))
                return principal;

            // 3. LÓGICA DE IDENTIDAD HÍBRIDA (Leída desde Key Vault / Config)
            var coemTenantId = _config["CmpSettings:CoemTenantId"];

            if (string.IsNullOrEmpty(coemTenantId))
            {
                _logger.LogCritical("Zenith Alert: 'CmpSettings:CoemTenantId' no está configurado en Key Vault/Appsettings.");
            }

            if (msTenantIdClaim.Equals(coemTenantId, StringComparison.OrdinalIgnoreCase))
            {
                // ES STAFF DE COEM: Acceso Global
                newIdentity.AddClaim(new Claim(ClaimTypes.Role, "CoemStaff"));
                newIdentity.AddClaim(new Claim("CmpScope", "Global"));
                _logger.LogDebug($"Usuario STAFF detectado: {email}");
            }
            else
            {
                // ES UN CLIENTE: Validamos existencia en nuestra base de datos
                var msTenantIdGuid = Guid.Parse(msTenantIdClaim);
                var tenant = await _context.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.MicrosoftTenantId == msTenantIdGuid);

                if (tenant != null)
                {
                    // Cliente reconocido y activo
                    newIdentity.AddClaim(new Claim(ClaimTypes.Role, "Customer"));
                    newIdentity.AddClaim(new Claim("CmpTenantId", tenant.Id.ToString()));
                    newIdentity.AddClaim(new Claim("CmpScope", "SingleTenant"));
                    _logger.LogDebug($"Usuario CLIENTE detectado para Tenant: {tenant.Name}");
                }
                else
                {
                    // Tenant no registrado o contrato inactivo
                    newIdentity.AddClaim(new Claim(ClaimTypes.Role, "Guest"));
                    _logger.LogWarning($"Intento de acceso de Tenant no registrado: {msTenantIdClaim}");
                }
            }

            // 4. SINCRONIZACIÓN JIT Y ROLES INTERNOS
            await SynchronizeInternalProfile(newIdentity, email, nameFromToken);

            return clone;
        }

        private async Task SynchronizeInternalProfile(ClaimsIdentity newIdentity, string email, string? nameFromToken)
        {
            var profile = await _context.UserProfiles
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Upn == email);

            if (profile != null)
            {
                // Sincronización de DisplayName si cambió en Entra ID
                if (!string.IsNullOrEmpty(nameFromToken) && profile.DisplayName != nameFromToken)
                {
                    profile.DisplayName = nameFromToken;
                    await _context.SaveChangesAsync();
                }

                // Inyectamos el rol granular de la base de datos (Admin, TAM, etc.)
                if (profile.Role != null)
                {
                    newIdentity.AddClaim(new Claim(ClaimTypes.Role, profile.Role.Name));
                }
            }
        }
    }
}