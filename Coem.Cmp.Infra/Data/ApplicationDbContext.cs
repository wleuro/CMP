using Coem.Cmp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coem.Cmp.Infra.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // --- DOMINIO CSP (COEM NATIVO) ---
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<PartnerCenterCredential> PartnerCenterCredentials { get; set; }

        // --- DOMINIO BYOT (GESTIÓN EXTERNA / ARM DIRECT) ---
        public DbSet<AzureDirectCredential> AzureDirectCredentials { get; set; }
        public DbSet<ExternalSubscription> ExternalSubscriptions { get; set; }

        // --- MOTOR FINANCIERO ZENITH (SILOS DE CONSUMO AISLADOS) ---
        public DbSet<PCUsageRecord> PCUsageRecords { get; set; }
        public DbSet<ExternalUsageRecord> ExternalUsageRecords { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. TENANTS: Unicidad para evitar duplicados de identidad
            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.MicrosoftTenantId)
                .IsUnique();

            // 2. SUBSCRIPTIONS (CSP NATIVO)
            modelBuilder.Entity<Subscription>(entity =>
            {
                entity.HasIndex(s => s.Id);
                // Precisión del 5,4 permite guardar porcentajes como 0.1500 (15%)
                entity.Property(s => s.Markup).HasPrecision(5, 4);
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
                entity.Property(e => e.Markup).HasPrecision(5, 4); // Margen para gestión externa
            });

            // 4. AZURE DIRECT CREDENTIALS (BYOT)
            modelBuilder.Entity<AzureDirectCredential>()
                .HasIndex(a => a.TenantId);

            // 5. SILOS DE CONSUMO: Invocamos el constructor maestro para ambos
            ConfigureUsageSilo<PCUsageRecord>(modelBuilder);
            ConfigureUsageSilo<ExternalUsageRecord>(modelBuilder);
        }

        /// <summary>
        /// Aplica las reglas estrictas de FinOps a cualquier tabla de consumo
        /// </summary>
        private void ConfigureUsageSilo<T>(ModelBuilder modelBuilder) where T : UsageRecordBase
        {
            modelBuilder.Entity<T>(entity =>
            {
                entity.HasKey(e => e.Id);

                // Índices críticos para que el portal cargue rápido los gráficos
                entity.HasIndex(e => new { e.SubscriptionId, e.UsageDate });
                entity.HasIndex(e => e.UsageDate);

                // Precisión financiera: 4 decimales para evitar fugas en el volumen
                entity.Property(e => e.Quantity).HasPrecision(18, 4);
                entity.Property(e => e.EstimatedCost).HasPrecision(18, 4);
                entity.Property(e => e.BilledCost).HasPrecision(18, 4);
                entity.Property(e => e.MarkupPercentage).HasPrecision(5, 4);

                // Nota Arquitectónica: Ya no atamos el consumo al TenantId directamente.
                // El modelo relacional correcto es: Usage -> Subscription -> Tenant.
                // Esto normaliza la base de datos y la hace mucho más robusta.
            });
        }
    }
}