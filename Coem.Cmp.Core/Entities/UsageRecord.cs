namespace Coem.Cmp.Core.Entities
{
    public class UsageRecord
    {
        public int Id { get; set; }
        public int TenantId { get; set; } // FK a nuestra tabla de Tenants
        public string SubscriptionId { get; set; }
        public string ResourceId { get; set; }
        public string ResourceName { get; set; }
        public string ResourceCategory { get; set; } // Compute, Storage, etc.
        public string ResourceSubCategory { get; set; }

        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal EstimatedCost { get; set; }
        public string Currency { get; set; }

        public DateTime UsageDate { get; set; } // El "Cuándo" ocurrió el consumo
        public DateTime ProcessedDate { get; set; } // El "Cuándo" lo trajo nuestro motor

        // Relación
        public Tenant Tenant { get; set; }
    }
}