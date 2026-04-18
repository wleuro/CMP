using Coem.Cmp.Core.Entities;
using Coem.Cmp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Coem.Cmp.Infra.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly ITenantContext? _tenantContext;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext? tenantContext = null)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        public string CurrentScope => _tenantContext?.Scope ?? "Global";
        public int? CurrentTenantId => _tenantContext?.CurrentTenantId;
        public string? CurrentCountry => _tenantContext?.CurrentCountry;

        // --- DOMINIO CSP (COEM NATIVO) ---
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<PartnerCenterCredential> PartnerCenterCredentials { get; set; }
        public DbSet<CostRecord> CostRecords { get; set; }

        // --- DOMINIO BYOT (GESTIÓN EXTERNA / ARM DIRECT) ---
        public DbSet<AzureDirectCredential> AzureDirectCredentials { get; set; }
        public DbSet<ExternalSubscription> ExternalSubscriptions { get; set; }

        // --- MOTOR FINANCIERO (SILOS DE CONSUMO AISLADOS) ---
        public DbSet<PCUsageRecord> PCUsageRecords { get; set; }
        public DbSet<ExternalUsageRecord> ExternalUsageRecords { get; set; }

        // --- MOTOR DE SEGURIDAD Y GOBERNANZA (RBAC) ---
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }

        // --- GESTIÓN DE CATEGORÍAS ---
        public DbSet<CategoryDefinition> CategoryDefinitions { get; set; }
        public DbSet<CategoryMapping> CategoryMappings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ====================================================================
            // LÓGICA DE AISLAMIENTO: GLOBAL QUERY FILTERS
            // ====================================================================

            modelBuilder.Entity<Tenant>().HasQueryFilter(t =>
                CurrentScope == "Global" ||
                (CurrentScope == "Regional" && t.Country == CurrentCountry) ||
                (CurrentScope == "SingleTenant" && t.Id == CurrentTenantId)
            );

            modelBuilder.Entity<Subscription>().HasQueryFilter(s =>
                CurrentScope == "Global" ||
                (CurrentScope == "Regional" && s.Tenant != null && s.Tenant.Country == CurrentCountry) ||
                (CurrentScope == "SingleTenant" && s.TenantId == CurrentTenantId)
            );

            modelBuilder.Entity<UserProfile>().HasQueryFilter(u =>
                CurrentScope == "Global" ||
                (CurrentScope == "Regional" && u.Tenant != null && u.Tenant.Country == CurrentCountry) ||
                (CurrentScope == "SingleTenant" && u.TenantId == CurrentTenantId)
            );

            modelBuilder.Entity<ExternalSubscription>().HasQueryFilter(e =>
                CurrentScope == "Global" ||
                (CurrentScope == "Regional" && e.Tenant != null && e.Tenant.Country == CurrentCountry) ||
                (CurrentScope == "SingleTenant" && e.TenantId == CurrentTenantId)
            );

            modelBuilder.Entity<CostRecord>().HasQueryFilter(c =>
                CurrentScope == "Global" ||
                (CurrentScope == "Regional" && c.Tenant != null && c.Tenant.Country == CurrentCountry) ||
                (CurrentScope == "SingleTenant" && c.TenantId == CurrentTenantId)
            );

            // ====================================================================
            // CONFIGURACIONES DE ENTIDADES
            // ====================================================================

            // 1. TENANTS: Unicidad y Navegación
            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.HasIndex(t => t.MicrosoftTenantId).IsUnique();

                // Relación con Usuarios (configurada desde el otro lado)
            });

            // 2. SUBSCRIPTIONS
            modelBuilder.Entity<Subscription>(entity =>
            {
                entity.HasIndex(s => s.Id);
                entity.Property(s => s.Markup).HasPrecision(5, 4);
            });

            // 3. COST RECORDS
            modelBuilder.Entity<CostRecord>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.ProviderCost).HasPrecision(18, 4);
                entity.Property(c => c.RetailAmount).HasPrecision(18, 4);

                entity.HasOne(c => c.Tenant)
                      .WithMany()
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(c => new { c.TenantId, c.UsageDate });
            });

            // 4. IDENTIDADES (UserProfile)
            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.HasKey(u => u.Id);
                entity.HasIndex(u => u.Upn).IsUnique();

                // Navegación bidireccional con Tenant para autogestión
                entity.HasOne(u => u.Tenant)
                      .WithMany(t => t.UserProfiles)
                      .HasForeignKey(u => u.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(u => u.Role)
                      .WithMany(r => r.Users)
                      .HasForeignKey(u => u.RoleId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // 5. SEGURIDAD (RBAC)
            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.HasKey(rp => new { rp.RoleId, rp.PermissionId });

                entity.HasOne(rp => rp.Role)
                      .WithMany(r => r.RolePermissions)
                      .HasForeignKey(rp => rp.RoleId);

                entity.HasOne(rp => rp.Permission)
                      .WithMany(p => p.RolePermissions)
                      .HasForeignKey(rp => rp.PermissionId);
            });

            // 6. SILOS DE CONSUMO
            ConfigureUsageSilo<PCUsageRecord>(modelBuilder);
            ConfigureUsageSilo<ExternalUsageRecord>(modelBuilder);
        }

        private void ConfigureUsageSilo<T>(ModelBuilder modelBuilder) where T : UsageRecordBase
        {
            modelBuilder.Entity<T>(entity =>
            {
                entity.HasQueryFilter(e =>
                    CurrentScope == "Global" ||
                    (CurrentScope == "Regional" && e.Tenant != null && e.Tenant.Country == CurrentCountry) ||
                    (CurrentScope == "SingleTenant" && e.TenantId == CurrentTenantId)
                );

                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TenantId, e.UsageDate, e.SubscriptionId })
                      .IncludeProperties(e => new { e.BilledCost, e.EstimatedCost, e.ResourceName, e.ChargeType });

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.Quantity).HasPrecision(18, 4);
                entity.Property(e => e.EstimatedCost).HasPrecision(18, 4);
                entity.Property(e => e.BilledCost).HasPrecision(18, 4);
                entity.Property(e => e.MarkupPercentage).HasPrecision(5, 4);
            });
        }
    }
}