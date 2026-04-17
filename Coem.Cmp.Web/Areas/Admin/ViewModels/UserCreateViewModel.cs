using System.ComponentModel.DataAnnotations;

namespace Coem.Cmp.Web.Areas.Admin.ViewModels
{
    public class UserCreateViewModel
    {
        [Required(ErrorMessage = "El UPN (Correo de Entra ID) es obligatorio.")]
        [EmailAddress(ErrorMessage = "Formato de correo inválido.")]
        [Display(Name = "Correo Electrónico (Entra ID)")]
        public required string Upn { get; set; }

        [Required(ErrorMessage = "Debes seleccionar un nivel de acceso.")]
        [Display(Name = "Rol")]
        public int RoleId { get; set; }

        // El nombre del rol se usa en el frontend para la lógica JS
        public string? RoleName { get; set; }

        [Display(Name = "País (Solo Comerciales)")]
        public string? Country { get; set; }

        [Display(Name = "Organización (Solo Clientes)")]
        public int? TenantId { get; set; }
    }
}