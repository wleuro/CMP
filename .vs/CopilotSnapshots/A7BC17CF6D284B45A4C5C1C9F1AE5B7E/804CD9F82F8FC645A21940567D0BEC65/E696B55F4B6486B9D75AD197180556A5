using Coem.Cmp.Infra.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Coem.Cmp.Web.Security
{
    public class AdminTenantAuthorizationFilter : IAsyncActionFilter
    {
        private readonly ApplicationDbContext _context;
        private readonly Guid _coemMasterTenantGuid; // Ahora es un Guid

        public AdminTenantAuthorizationFilter(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;

            // Parseamos el string del Key Vault al momento de instanciar el filtro
            string tenantIdString = configuration["AzureAd:TenantId"];
            Guid.TryParse(tenantIdString, out _coemMasterTenantGuid);
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var routeData = context.RouteData.Values;
            if (routeData.TryGetValue("area", out var area) &&
                area?.ToString()?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true)
            {
                var userEmail = context.HttpContext.User.Identity?.Name;

                var userProfile = await _context.UserProfiles
                    .Include(u => u.Tenant)
                    .FirstOrDefaultAsync(u => u.Upn == userEmail);

                // Comparamos Guid de la DB contra Guid del KeyVault
                if (userProfile?.Tenant == null ||
                    userProfile.Tenant.MicrosoftTenantId != _coemMasterTenantGuid)
                {
                    context.Result = new ForbidResult();
                    return;
                }
            }

            await next();
        }
    }
}