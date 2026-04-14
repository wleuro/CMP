using System.Collections.Generic;

namespace Coem.Cmp.Core.Entities
{
    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsSystemRole { get; set; } // Evita que alguien borre roles críticos

        public ICollection<RolePermission> RolePermissions { get; set; }
        public ICollection<UserProfile> Users { get; set; }
    }
}