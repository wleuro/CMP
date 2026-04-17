using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Coem.Cmp.Core.Entities
{
    public class Tenant
    {
        public int Id { get; set; }

        [Required]
        public Guid MicrosoftTenantId { get; set; }

        [Required]
        [MaxLength(200)]
        public required string Name { get; set; }

        [MaxLength(100)]
        public required string DefaultDomain { get; set; }

        // El núcleo de la flexibilidad contractual
        [Required]
        [MaxLength(20)]
        public string AgreementType { get; set; } = "CSP"; // Valores permitidos: "CSP", "EA", "MCA"

        // Para EA, a veces el ID de facturación (Enrollment Number) es diferente al Tenant ID
        [MaxLength(100)]
        public string? BillingAccountId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Country { get; set; } = "Colombia";

        public bool IsActive { get; set; } = true;
        public DateTime OnboardingDate { get; set; } = DateTime.UtcNow;

        // ⚠️ PURGADO: La propiedad CostRecords fue eliminada para evitar Shadow States 
        // y proteger la memoria RAM. El consumo se consulta directo en los Silos (UsageRecords).

        // ¿Controles Empresariales le cobra el consumo a este cliente?
        // true = Vas a Partner Center a traer sus costos.
        // false = Vas por Azure Lighthouse directamente a su suscripción.
        public bool IsBilledByCoem { get; set; } = true;

        // Si el cliente es externo (IsBilledByCoem = false), necesitas el ID del directorio 
        // donde vas a inyectar las políticas de Lighthouse.
        public Guid? ExternalDirectoryId { get; set; }

        // LA INYECCIÓN: Relación 1 a N con Subscriptions
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    }
}