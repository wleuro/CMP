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

        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<PartnerCenterCredential> PartnerCenterCredentials { get; set; }

        // El corazón del motor FinOps: Consumo diario detallado
        public DbSet<UsageRecord> UsageRecords { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. TENANTS: Unicidad para evitar duplicados de identidad
            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.MicrosoftTenantId)
                .IsUnique();

            // 2. USAGE RECORDS: Optimización para analítica de alto nivel
            modelBuilder.Entity<UsageRecord>(entity =>
            {
                // Índice compuesto: Crucial para reportes rápidos por cliente y rango de tiempo
                entity.HasIndex(e => new { e.TenantId, e.UsageDate });

                // Índice simple: Para el Dashboard global de la región
                entity.HasIndex(e => e.UsageDate);

                // Precisión FinOps: 4 decimales evitan fugas financieras en el acumulado mensual
                entity.Property(e => e.EstimatedCost).HasPrecision(18, 4);
                entity.Property(e => e.Quantity).HasPrecision(18, 4);

                // Relación explícita
                entity.HasOne(e => e.Tenant)
                      .WithMany() // O .WithMany(t => t.UsageRecords) si agregas la colección en la entidad Tenant
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // 3. SUBSCRIPTIONS: Indexamos por ID para el Discovery de la Azure Function
            modelBuilder.Entity<Subscription>()
                .HasIndex(s => s.Id);
        }
    }
}