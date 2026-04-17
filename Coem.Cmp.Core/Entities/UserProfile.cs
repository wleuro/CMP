using System;

namespace Coem.Cmp.Core.Entities
{
    public class UserProfile
    {
        // 🛡️ ZENITH: Cambiamos de Guid a int para consistencia con Role y Tenant
        public int Id { get; set; }

        public required string Upn { get; set; }
        public required string DisplayName { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Country { get; set; }

        public int RoleId { get; set; }
        public Role? Role { get; set; }

        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }
    }
}