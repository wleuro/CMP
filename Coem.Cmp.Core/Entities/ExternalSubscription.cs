namespace Coem.Cmp.Core.Entities
{
    public class ExternalSubscription
    {
        public Guid Id { get; set; } // El SubscriptionId de Azure
        public int AzureDirectCredentialId { get; set; } // Relación con la llave que la trajo
        public int? TenantId { get; set; } // Relación con el Tenant
        public required string Name { get; set; }
        public required string Status { get; set; }
        public required string AuditResult { get; set; } // "Cost:OK|Read:FAIL"
        public DateTime LastSync { get; set; }
        public decimal Markup { get; set; }

        public AzureDirectCredential? Credential { get; set; }
        public Tenant? Tenant { get; set; } // Navegación a Tenant
    }
}