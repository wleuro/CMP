namespace Coem.Cmp.Core.Entities
{
    public class ExternalSubscription
    {
        public Guid Id { get; set; } // El SubscriptionId de Azure
        public int AzureDirectCredentialId { get; set; } // Relación con la llave que la trajo
        public string Name { get; set; }
        public string Status { get; set; }
        public string AuditResult { get; set; } // "Cost:OK|Read:FAIL"
        public DateTime LastSync { get; set; }

        public AzureDirectCredential Credential { get; set; }
    }
}