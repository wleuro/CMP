namespace Coem.Cmp.Core.Entities
{
    public class PartnerCenterCredential
    {
        public int Id { get; set; }
        public string CountryName { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string? MpnId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? LastSyncSuccess { get; set; }
    }
}