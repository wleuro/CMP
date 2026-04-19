using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Coem.Cmp.Web.Components
{
    public class UserProfileViewComponent : ViewComponent
    {
        private readonly GraphServiceClient _graphServiceClient;

        public UserProfileViewComponent(GraphServiceClient graphServiceClient)
        {
            _graphServiceClient = graphServiceClient;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var user = User as ClaimsPrincipal;

            var viewModel = new UserProfileViewModel
            {
                DisplayName = user?.FindFirst("name")?.Value ?? user?.Identity?.Name ?? "Usuario",
                Role = user?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "Invitado"
            };

            // Cálculo de iniciales (Failsafe inmediato)
            viewModel.Initials = GetInitials(viewModel.DisplayName);

            if (user == null || !user.Identity.IsAuthenticated)
            {
                return View(viewModel);
            }

            try
            {
                // Intentamos obtener la foto. 
                // En un ViewComponent, si el token se perdió (por reinicio de dotnet watch),
                // la llamada fallará con MsalUiRequiredException o ServiceException.
                using (var photoStream = await _graphServiceClient.Me.Photo.Content.GetAsync())
                {
                    if (photoStream != null)
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            await photoStream.CopyToAsync(memoryStream);
                            viewModel.PhotoBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(memoryStream.ToArray())}";
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallback silencioso. 
                // Si el token es inválido o no hay foto, mostramos iniciales. 
                // No permitas que la infraestructura de Microsoft rompa tu interfaz.
                viewModel.PhotoBase64 = null;
            }

            return View(viewModel);
        }

        private string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "U";
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            }
            return parts[0][0].ToString().ToUpper();
        }
    }

    public class UserProfileViewModel
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public string? PhotoBase64 { get; set; }
        public string Role { get; set; } = string.Empty;
    }
}