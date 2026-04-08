using System;

namespace Coem.Cmp.Web.Areas.Admin.ViewModels
{
    public class UserListViewModel
    {
        // EL CAMBIO CRÍTICO: Debe ser Guid para coincidir con tu UserProfile
        public Guid Id { get; set; }

        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string RoleName { get; set; }
        public string TenantName { get; set; }
        public bool IsActive { get; set; }

        public int RoleId { get; set; }      // int (conforme a tu captura)
        public int TenantId { get; set; }    // int (conforme a tu captura)

        public bool IsInternalTeam => !string.IsNullOrEmpty(TenantName) && TenantName.Contains("Controles Empresariales");
    }
}