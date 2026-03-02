using Coem.Cmp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace Coem.Cmp.Infra.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<CostRecord> CostRecords { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Índices para velocidad en reportes (Zenith Performance)
            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.MicrosoftTenantId)
                .IsUnique();

            modelBuilder.Entity<CostRecord>()
                .HasIndex(c => new { c.TenantId, c.UsageDate });
        }
    }
}