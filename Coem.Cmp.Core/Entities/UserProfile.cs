using System;

namespace Coem.Cmp.Core.Entities
{
    public class UserProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Upn { get; set; } // El UserPrincipalName o Email que llega de Entra ID
        public required string DisplayName { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Country { get; set; } // País del usuario para filtrado regional

        public int RoleId { get; set; }
        public Role? Role { get; set; }

        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }
    }
}