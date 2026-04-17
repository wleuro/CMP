using System;

namespace Coem.Cmp.Web.Areas.Admin.ViewModels
{
    public class UserListViewModel
    {
        // 🛡️ Ajustado a int para que no choque con la DB
        public int Id { get; set; }

        public required string Email { get; set; }
        public required string DisplayName { get; set; }
        public required string RoleName { get; set; }
        public required string TenantName { get; set; }
        public bool IsActive { get; set; }

        public int RoleId { get; set; }
        public int TenantId { get; set; }

        // Esta es la propiedad que el compilador dice que te falta
        public bool IsInternalTeam => !string.IsNullOrEmpty(TenantName) &&
                                     (TenantName.Contains("Controles Empresariales") ||
                                      TenantName.Contains("Coem"));
    }
}