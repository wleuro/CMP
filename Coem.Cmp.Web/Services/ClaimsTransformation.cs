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
                _logger.LogCritical("Alerta de Configuración: 'CmpSettings:CoemTenantId' no está configurado en Key Vault/Appsettings.");
            }

            if (msTenantIdClaim.Equals(coemTenantId, StringComparison.OrdinalIgnoreCase))
            {
                // ES STAFF DE COEM: Asignamos Global por defecto (Se ajustará en el Paso 4 si es necesario)
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

            // 4. SINCRONIZACIÓN JIT Y AJUSTE DE ROLES INTERNOS
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

                if (profile.Role != null)
                {
                    // 🛡️ ZENITH: Inyectamos el Rol estándar y el CmpRole Normalizado (sin espacios) para las Policies
                    newIdentity.AddClaim(new Claim(ClaimTypes.Role, profile.Role.Name));
                    newIdentity.AddClaim(new Claim("CmpRole", profile.Role.Name.Replace(" ", "")));

                    // 🛡️ ZENITH: PODER ABSOLUTO PARA GLOBAL ADMIN
                    if (profile.Role.Name == "Global Admin" || profile.Role.Name == "GlobalAdmin")
                    {
                        newIdentity.AddClaim(new Claim("CmpPermission", "Markup_Write"));
                        newIdentity.AddClaim(new Claim("CmpPermission", "Tenant_Setup"));
                        // El scope ya es 'Global' por el paso 3
                    }

                    // 🛡️ ZENITH: PERMISOS ESPECÍFICOS PARA OPERACIONES
                    if (profile.Role.Name == "Operaciones")
                    {
                        newIdentity.AddClaim(new Claim("CmpPermission", "Markup_Write"));
                        newIdentity.AddClaim(new Claim("CmpPermission", "Tenant_Setup"));
                    }

                    // 🛡️ ZENITH: FILTRO TERRITORIAL PARA COMERCIALES
                    if (profile.Role.Name == "Comercial")
                    {
                        // Removemos el acceso global que se le dio en el Paso 3
                        var scopeClaim = newIdentity.FindFirst("CmpScope");
                        if (scopeClaim != null)
                        {
                            newIdentity.RemoveClaim(scopeClaim);
                        }

                        // Asignamos el alcance regional y su país específico
                        newIdentity.AddClaim(new Claim("CmpScope", "Regional"));

                        if (!string.IsNullOrEmpty(profile.Country))
                        {
                            newIdentity.AddClaim(new Claim("CmpCountry", profile.Country));
                        }
                    }
                }
            }
        }
    }
}