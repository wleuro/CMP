using System.Collections.Generic;

namespace Coem.Cmp.Core.Entities
{
    public class Permission
    {
        public int Id { get; set; }
        public string SystemName { get; set; } // Ej: "Assessment.Create", "Cost.ViewBuyPrice"
        public string DisplayName { get; set; }
        public string Module { get; set; } // Ej: "Billing", "Identity", "BYOT"

        public ICollection<RolePermission> RolePermissions { get; set; }
    }
}