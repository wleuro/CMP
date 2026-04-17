using Coem.Cmp.Core.Entities;
using Coem.Cmp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Coem.Cmp.Infra.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly ITenantContext? _tenantContext;

        // Inyectamos el contexto de identidad. 
        // Lo hacemos opcional (null) para que el Worker o las Migraciones (que no tienen usuario web) no fallen.
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext? tenantContext = null)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        // Propiedades de evaluación segura: EF Core las lee en tiempo real para armar el SQL.
        // Si no hay contexto (Worker), asumimos "Global" para que el proceso corra sin bloqueos.
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
        // --- GEstion de categorias ---
        public DbSet<CategoryDefinition> CategoryDefinitions { get; set; }

        // --- Categorias De Productos CSP --- 
        public DbSet<CategoryMapping> CategoryMappings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ====================================================================
            // 🛡️ LÓGICA ZENITH: GLOBAL QUERY FILTERS (AISLAMIENTO DE DATOS)
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
            // CONFIGURACIONES EXISTENTES (MANTENIDAS INTACTAS)
            // ====================================================================

            // 1. TENANTS: Unicidad para evitar duplicados de identidad
            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.MicrosoftTenantId)
                .IsUnique();

            // 2. SUBSCRIPTIONS (CSP NATIVO)
            modelBuilder.Entity<Subscription>(entity =>
            {
                entity.HasIndex(s => s.Id);
                entity.Property(s => s.Markup).HasPrecision(5, 4);
            });

            // 2.5. COST RECORDS (REGISTROS DE COSTOS CSP)
            modelBuilder.Entity<CostRecord>(entity =>
            {
                entity.HasKey(c => c.Id);
                
                // Configuración de precisión financiera
                entity.Property(c => c.ProviderCost).HasPrecision(18, 4);
                entity.Property(c => c.RetailAmount).HasPrecision(18, 4);
                
                // Relación con Tenant
                entity.HasOne(c => c.Tenant)
                      .WithMany()
                      .OnDelete(DeleteBehavior.Restrict);
                
                // Índice para consultas frecuentes
                entity.HasIndex(c => new { c.TenantId, c.UsageDate });
            });

            // 3. EXTERNAL SUBSCRIPTIONS (BYOT)
            modelBuilder.Entity<ExternalSubscription>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Credential)
                      .WithMany()
                      .HasForeignKey(e => e.AzureDirectCredentialId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.AzureDirectCredentialId);
                entity.Property(e => e.Markup).HasPrecision(5, 4);
            });

            // 4. AZURE DIRECT CREDENTIALS (BYOT)
            modelBuilder.Entity<AzureDirectCredential>()
                .HasIndex(a => a.TenantId);

            // --- CONFIGURACIÓN DE SEGURIDAD ---

            // Llave compuesta para la tabla transaccional de Permisos
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

            // 5. IDENTIDADES (UserProfile consolidado)
            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.HasKey(u => u.Id);

                // Unicidad Absoluta de Identidad
                entity.HasIndex(u => u.Upn).IsUnique();

                // Aislamiento Multi-Tenant (Protección de Datos)
                entity.HasOne(u => u.Tenant)
                      .WithMany()
                      .HasForeignKey(u => u.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                // Relación con Rol - Configuración explícita
                entity.HasOne(u => u.Role)
                      .WithMany(r => r.Users)
                      .HasForeignKey(u => u.RoleId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // 6. SILOS DE CONSUMO (Aplica a PC y BYOT)
            ConfigureUsageSilo<PCUsageRecord>(modelBuilder);
            ConfigureUsageSilo<ExternalUsageRecord>(modelBuilder);
        }

        /// <summary>
        /// Aplica las reglas estrictas de FinOps y Aislamiento a cualquier tabla de consumo
        /// </summary>
        private void ConfigureUsageSilo<T>(ModelBuilder modelBuilder) where T : UsageRecordBase
        {
            modelBuilder.Entity<T>(entity =>
            {
                // 🛡️ ZENITH: Filtro Global aplicado dinámicamente a tablas de consumo masivo
                entity.HasQueryFilter(e =>
                    CurrentScope == "Global" ||
                    (CurrentScope == "Regional" && e.Tenant != null && e.Tenant.Country == CurrentCountry) ||
                    (CurrentScope == "SingleTenant" && e.TenantId == CurrentTenantId)
                );

                entity.HasKey(e => e.Id);

                // --- 1. EL ÍNDICE DIOS (Covering Index) ---
                entity.HasIndex(e => new { e.TenantId, e.UsageDate, e.SubscriptionId })
                      .IncludeProperties(e => new { e.BilledCost, e.EstimatedCost, e.ResourceName, e.ChargeType });

                // --- 2. ÍNDICES FINOPS ---
                entity.HasIndex(e => e.FinOpsEnvironment);
                entity.HasIndex(e => e.FinOpsCostCenter);

                // --- 3. AISLAMIENTO MULTI-TENANT ---
                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                // --- 4. PRECISIÓN FINANCIERA ---
                entity.Property(e => e.Quantity).HasPrecision(18, 4);
                entity.Property(e => e.EstimatedCost).HasPrecision(18, 4);
                entity.Property(e => e.BilledCost).HasPrecision(18, 4);
                entity.Property(e => e.MarkupPercentage).HasPrecision(5, 4);
            });
        }
    }
}