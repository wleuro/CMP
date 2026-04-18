using System;

namespace Coem.Cmp.Core.Entities
{
    public class Subscription
    {
        // 🛡️ Identificador interno (int) para consistencia con Tenant y UserProfile
        public int Id { get; set; }

        // El ID real de Microsoft lo guardamos como referencia, no como PK
        public Guid MicrosoftSubscriptionId { get; set; }

        public int TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public required string OfferId { get; set; }

        // Estandarizamos a 'Name' para que la UI sea limpia
        public required string Name { get; set; }

        public required string Status { get; set; }

        // Lógica de negocio encapsulada
        public bool IsActive => Status.Equals("active", StringComparison.OrdinalIgnoreCase);

        public bool IsAzureWorkload { get; set; }
        public DateTime CreatedDate { get; set; }

        public required string Category { get; set; } // "AP", "AL" o "Colab"
        public decimal Markup { get; set; }
        public DateTime? EffectiveDate { get; set; }

        // Módulo SaaS / Licenciamiento
        public int Quantity { get; set; }
        public string? BillingCycle { get; set; }

        // Propiedad calculada para filtros rápidos en código
        public bool IsSaaS => Category != "AZ";
    }
}