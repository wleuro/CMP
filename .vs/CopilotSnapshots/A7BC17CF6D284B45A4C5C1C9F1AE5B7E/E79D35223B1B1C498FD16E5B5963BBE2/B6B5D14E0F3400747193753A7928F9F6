using Coem.Cmp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; // Necesario para leer el Key Vault
using System;
using System.Linq;

namespace Coem.Cmp.Infra.Data
{
    public static class DbInitializer
    {
        // Actualizamos la firma para inyectar IConfiguration
        public static void Initialize(ApplicationDbContext context, IConfiguration config)
        {
            // Aplica migraciones pendientes automáticamente. 
            context.Database.Migrate();

            // 1. LEER EL KEY VAULT Y ASEGURAR EL TENANT MAESTRO (COEM)
            string configTenantId = config["AzureAd:TenantId"];

            // Forzamos la conversión a GUID seguro
            if (!Guid.TryParse(configTenantId, out Guid coemMasterTenantGuid))
            {
                throw new Exception($"CRÍTICO: El TenantId en KeyVault no es válido o está vacío. Valor actual: '{configTenantId}'");
            }

            // Ahora comparamos Guid con Guid
            var coemTenant = context.Tenants.FirstOrDefault(t => t.MicrosoftTenantId == coemMasterTenantGuid);
            if (coemTenant == null)
            {
                coemTenant = new Tenant
                {
                    MicrosoftTenantId = coemMasterTenantGuid,
                    Name = "Controles Empresariales (Admin)",
                    DefaultDomain = "coem.co", // Corregido según tu clase Tenant.cs
                    AgreementType = "CSP",     // Campo requerido en tu clase
                    Country = "Colombia"       // Campo requerido en tu clase
                };
                context.Tenants.Add(coemTenant);
                context.SaveChanges();
            }

            // 2. INYECTAR LA MATRIZ DE ROLES (Solo si está vacía)
            if (!context.Roles.Any())
            {
                var roles = new Role[]
                {
                    // Roles Internos (COEM)
                    new Role { Name = "Global Admin", Description = "Control total y configuración maestra", IsSystemRole = true },
                    new Role { Name = "Executive", Description = "Visión macro del Negocio y cuotas", IsSystemRole = true },
                    new Role { Name = "FinOps Global", Description = "Auditoría de márgenes y reconciliación", IsSystemRole = true },
                    new Role { Name = "TAM", Description = "Salud técnica, adopción y prospección BYOT", IsSystemRole = true },
                    new Role { Name = "Preventa / Arq.", Description = "Diseño de topologías y optimización", IsSystemRole = true },
                    new Role { Name = "Comercial", Description = "Facturación final y estado de contratos", IsSystemRole = true },
                    new Role { Name = "Soporte TI", Description = "Mantenimiento técnico sin visibilidad financiera", IsSystemRole = false },
                    
                    // Roles Externos (Clientes)
                    new Role { Name = "Client Admin", Description = "Autogestión total de su Tenant", IsSystemRole = true },
                    new Role { Name = "Client Financial", Description = "Facturación y ahorro", IsSystemRole = false },
                    new Role { Name = "Client IT / Ops", Description = "Inventario, alertas y Tags", IsSystemRole = false },
                    new Role { Name = "Client Viewer", Description = "Solo lectura de consumo general", IsSystemRole = true }
                };

                context.Roles.AddRange(roles);
                context.SaveChanges();
            }

            // 3. CREAR TU PERFIL MAESTRO (CON SELLO DE SEGURIDAD ABSOLUTO)
            var globalAdminRole = context.Roles.FirstOrDefault(r => r.Name == "Global Admin");
            string myCorpEmail = "wleuro@coem.co";

            if (globalAdminRole != null && !context.UserProfiles.Any(u => u.Upn == myCorpEmail))
            {
                var willProfile = new UserProfile
                {
                    Upn = myCorpEmail,
                    DisplayName = "William Leuro Velandia",
                    IsActive = true,
                    RoleId = globalAdminRole.Id,
                    TenantId = coemTenant.Id // <- REGLA ZERO TRUST: Amarrado al Tenant Maestro.
                };

                context.UserProfiles.Add(willProfile);
                context.SaveChanges();
            }
        }
    }
}