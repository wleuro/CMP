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
        public DbSet<UsageRecord> UsageRecords { get; set; }

        // --- DOMINIO BYOT (GESTIÓN EXTERNA / ARM DIRECT) ---
        public DbSet<AzureDirectCredential> AzureDirectCredentials { get; set; }
        public DbSet<ExternalSubscription> ExternalSubscriptions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. TENANTS: Unicidad para evitar duplicados de identidad
            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.MicrosoftTenantId)
                .IsUnique();

            // 2. USAGE RECORDS: Optimización FinOps
            modelBuilder.Entity<UsageRecord>(entity =>
            {
                entity.HasIndex(e => new { e.TenantId, e.UsageDate });
                entity.HasIndex(e => e.UsageDate);
                entity.Property(e => e.EstimatedCost).HasPrecision(18, 4);
                entity.Property(e => e.Quantity).HasPrecision(18, 4);

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // 3. SUBSCRIPTIONS (CSP): Indexamos por ID para Discovery
            modelBuilder.Entity<Subscription>()
                .HasIndex(s => s.Id);

            // 4. AZURE DIRECT CREDENTIALS (BYOT)
            modelBuilder.Entity<AzureDirectCredential>()
                .HasIndex(a => a.TenantId);

            // 5. EXTERNAL SUBSCRIPTIONS (BYOT): Aislamiento Total
            modelBuilder.Entity<ExternalSubscription>(entity =>
            {
                // Clave primaria es el GUID de Azure
                entity.HasKey(e => e.Id);

                // Relación con la credencial que tiene el acceso
                entity.HasOne(e => e.Credential)
                      .WithMany()
                      .HasForeignKey(e => e.AzureDirectCredentialId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Índice para búsquedas rápidas por conector
                entity.HasIndex(e => e.AzureDirectCredentialId);
            });
        }
    }
}