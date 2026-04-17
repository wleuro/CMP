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
            int.TryParse(_httpContextAccessor.HttpContext?.User.FindFirst("CmpTenantId")?.Value, out var id) ? id : null;

        public string? CurrentCountry =>
            _httpContextAccessor.HttpContext?.User.FindFirst("CmpCountry")?.Value;

        public string? Role =>
            _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role)?.Value;

        public string Scope =>
            _httpContextAccessor.HttpContext?.User.FindFirst("CmpScope")?.Value ?? "Guest";

        public bool IsCoemStaff =>
            _httpContextAccessor.HttpContext?.User.IsInRole("CoemStaff") ?? false;
    }
}