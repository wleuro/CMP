using Coem.Cmp.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Coem.Cmp.Web.Services
{
    public class TenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TenantContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public int? CurrentTenantId =>
            int.TryParse(_httpContextAccessor.HttpContext?.User?.FindFirst("CmpTenantId")?.Value, out var id) ? id : null;

        public string? CurrentCountry =>
            _httpContextAccessor.HttpContext?.User?.FindFirst("CmpCountry")?.Value;

        public string? Role =>
            _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;

        // CORRECCIÓN: Se encapsula cada evaluación y se fuerza a false si el resultado es null
        public bool IsCoemStaff =>
            (_httpContextAccessor.HttpContext?.User?.IsInRole("CoemStaff") ?? false) ||
            (_httpContextAccessor.HttpContext?.User?.IsInRole("GlobalAdmin") ?? false);

        public string Scope
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                if (user == null) return "Guest";

                if (user.IsInRole("GlobalAdmin")) return "Global";

                if (CurrentTenantId.HasValue) return "SingleTenant";

                if (!string.IsNullOrEmpty(CurrentCountry)) return "Regional";

                return user.FindFirst("CmpScope")?.Value ?? "Guest";
            }
        }
    }
}