using Coem.Cmp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;

namespace Coem.Cmp.Infra.Data
{
    public static class DbInitializer
    {
        public static void Initialize(ApplicationDbContext context, IConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(config);

            // 1. APLICAR ESQUEMA
            context.Database.Migrate();

            // 2. TENANT MAESTRO (COEM)
            string configTenantId = config["AzureAd:TenantId"] ?? string.Empty;
            if (!Guid.TryParse(configTenantId, out Guid coemMasterTenantGuid))
            {
                throw new InvalidOperationException("CRÍTICO: TenantId no válido en la configuración.");
            }

            var coemTenant = context.Tenants.IgnoreQueryFilters().FirstOrDefault(t => t.MicrosoftTenantId == coemMasterTenantGuid);
            if (coemTenant == null)
            {
                coemTenant = new Tenant
                {
                    MicrosoftTenantId = coemMasterTenantGuid,
                    Name = "Controles Empresariales (Admin)",
                    DefaultDomain = "coem.co",
                    AgreementType = "CSP",
                    Country = "Colombia",
                    IsActive = true,
                    OnboardingDate = DateTime.UtcNow
                };
                context.Tenants.Add(coemTenant);
                context.SaveChanges();
            }

            // 3. MATRIZ DE ROLES (Verificación individual para evitar estados inconsistentes)
            string[] requiredRoles = { "GlobalAdmin", "Operaciones", "Comercial", "Customer" };
            foreach (var roleName in requiredRoles)
            {
                if (!context.Roles.IgnoreQueryFilters().Any(r => r.Name == roleName))
                {
                    context.Roles.Add(new Role
                    {
                        Name = roleName,
                        Description = $"Acceso nivel: {roleName}",
                        IsSystemRole = true
                    });
                }
            }
            context.SaveChanges();

            // 4. RESTAURAR TU ACCESO (Will)
            var globalAdminRole = context.Roles.IgnoreQueryFilters()
                .First(r => r.Name == "GlobalAdmin");

            const string MyCorpEmail = "wleuro@coem.co";
            var willProfile = context.UserProfiles.IgnoreQueryFilters().FirstOrDefault(u => u.Upn == MyCorpEmail);

            if (willProfile == null)
            {
                context.UserProfiles.Add(new UserProfile
                {
                    Upn = MyCorpEmail,
                    DisplayName = "William Leuro Velandia",
                    IsActive = true,
                    RoleId = globalAdminRole.Id,
                    TenantId = coemTenant.Id,
                    Country = "Colombia"
                });
                context.SaveChanges();
            }
            else if (willProfile.RoleId != globalAdminRole.Id)
            {
                // Corrección de deriva de permisos
                willProfile.RoleId = globalAdminRole.Id;
                context.SaveChanges();
            }

            // 5. SEED DE PRUEBA: EVIDENCIA SINGLE PANE (Nombre purgado)
            if (!context.Tenants.IgnoreQueryFilters().Any(t => t.Name == "Cliente Demo Corporativo"))
            {
                var demoTenant = new Tenant
                {
                    MicrosoftTenantId = Guid.NewGuid(),
                    Name = "Cliente Demo Corporativo",
                    DefaultDomain = "democorp.onmicrosoft.com",
                    Country = "Ecuador",
                    IsActive = true,
                    OnboardingDate = DateTime.UtcNow
                };
                context.Tenants.Add(demoTenant);
                context.SaveChanges();

                context.Subscriptions.AddRange(
                    new Subscription
                    {
                        TenantId = demoTenant.Id,
                        MicrosoftSubscriptionId = Guid.NewGuid(),
                        OfferId = "MS-O365-B-PREM",
                        Name = "Microsoft 365 Business Premium",
                        Category = "M365",
                        Status = "active",
                        Quantity = 15,
                        CreatedDate = DateTime.UtcNow,
                        Markup = 0.12m
                    },
                    new Subscription
                    {
                        TenantId = demoTenant.Id,
                        MicrosoftSubscriptionId = Guid.NewGuid(),
                        OfferId = "MS-AZR-PLAN",
                        Name = "Azure Plan",
                        Category = "AZ",
                        Status = "active",
                        Quantity = 1,
                        IsAzureWorkload = true,
                        CreatedDate = DateTime.UtcNow,
                        Markup = 0.03m
                    }
                );
                context.SaveChanges();
            }
        }
    }
}