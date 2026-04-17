using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coem.Cmp.Core.Entities
{
    public class CostRecord
    {
        public long Id { get; set; }

        [ForeignKey(nameof(Tenant))]
        public int TenantId { get; set; }
        public Tenant? Tenant { get; set; } // Navegación

        [Required]
        public Guid SubscriptionId { get; set; }

        public DateTime UsageDate { get; set; }

        [MaxLength(200)]
        public required string ResourceGroup { get; set; }

        [MaxLength(100)]
        public required string ServiceName { get; set; } // "Virtual Machines", "Storage"

        // COSTO REAL (Lo que pagas a Microsoft) - Privado
        [Column(TypeName = "decimal(18,4)")]
        public decimal ProviderCost { get; set; }

        // PRECIO VENTA (Lo que ve el cliente) - Público
        [Column(TypeName = "decimal(18,4)")]
        public decimal RetailAmount { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "USD";
    }
}