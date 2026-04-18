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

        [Required]
        [MaxLength(20)]
        public string AgreementType { get; set; } = "CSP"; // "CSP", "EA", "MCA"

        [MaxLength(100)]
        public string? BillingAccountId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Country { get; set; } = "Colombia";

        public bool IsActive { get; set; } = true;
        public DateTime OnboardingDate { get; set; } = DateTime.UtcNow;

        // Propiedades de facturación y acceso externo
        public bool IsBilledByCoem { get; set; } = true;
        public Guid? ExternalDirectoryId { get; set; }

        // RELACIONES

        // Relación con suscripciones de productividad y consumo
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

        // Relación con los perfiles de usuario que tienen acceso a este portal específico
        public ICollection<UserProfile> UserProfiles { get; set; } = new List<UserProfile>();
    }
}