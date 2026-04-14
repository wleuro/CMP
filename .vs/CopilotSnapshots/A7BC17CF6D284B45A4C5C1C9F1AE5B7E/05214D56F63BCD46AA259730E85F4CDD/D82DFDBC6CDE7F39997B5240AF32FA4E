using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using System.Threading.Tasks;

namespace Coem.Cmp.Web.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly GraphServiceClient _graphServiceClient;

        public ProfileController(GraphServiceClient graphServiceClient)
        {
            _graphServiceClient = graphServiceClient;
        }

        [HttpGet]
        public async Task<IActionResult> GetPhoto()
        {
            try
            {
                // Petición directa a Entra ID para traer la foto binaria del usuario en sesión
                var photoStream = await _graphServiceClient.Me.Photo.Content.GetAsync();

                if (photoStream != null)
                {
                    return File(photoStream, "image/jpeg");
                }
            }
            catch
            {
                // Silenciamos la excepción. Si el usuario no ha subido foto a su Office 365,
                // o si hay un problema de permisos, fallará limpiamente.
            }

            // Retorna un 404. Nuestro frontend ya está programado para interceptar esto 
            // y mostrar el círculo Magenta con sus iniciales como plan de contingencia.
            return NotFound();
        }
    }
}