using System;

namespace Coem.Cmp.Core.Entities
{
    public class UserProfile
    {
        public int Id { get; set; }

        public required string Upn { get; set; }
        public required string DisplayName { get; set; }

        // Atributos para la gestión del portal del cliente
        public string? JobTitle { get; set; }
        public string? PhoneNumber { get; set; }

        public bool IsActive { get; set; } = true;
        public string? Country { get; set; }

        // Trazabilidad de ingreso
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int RoleId { get; set; }
        public Role? Role { get; set; }

        // Identificador del cliente. Nulo para personal interno, con valor para usuarios finales.
        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }
    }
}