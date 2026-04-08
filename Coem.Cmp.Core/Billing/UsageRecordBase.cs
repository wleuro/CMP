using System;

namespace Coem.Cmp.Core.Entities
{
    public abstract class UsageRecordBase
    {
        public long Id { get; set; }

        // --- EL EJE MULTI-TENANT (Aplica para PC y BYOT) ---
        public int TenantId { get; set; }
        public Tenant Tenant { get; set; } // Propiedad de navegación

        public Guid SubscriptionId { get; set; }
        public DateTime UsageDate { get; set; }

        // --- PREPARACIÓN M365 Y AZURE NCE ---
        public string Publisher { get; set; } = "Microsoft"; // "Microsoft" o "Microsoft Corporation"
        public string ChargeType { get; set; } = "Usage"; // "Usage", "Purchase", o "Proration"

        public string ProductName { get; set; }
        public string MeterCategory { get; set; }
        public decimal Quantity { get; set; }

        // --- GRANULARIDAD ZENITH ---
        public string? ResourceId { get; set; }
        public string? ResourceName { get; set; }
        public string? TagsJson { get; set; }

        // --- COLUMNAS PROMOVIDAS FINOPS (Indexables) ---
        public string? FinOpsEnvironment { get; set; }
        public string? FinOpsCostCenter { get; set; }

        // --- MOTOR FINANCIERO ZENITH ---
        public decimal EstimatedCost { get; set; }
        public decimal MarkupPercentage { get; set; }
        public decimal BilledCost { get; set; }

        public string Currency { get; set; }
        public string ProviderSource { get; set; }
    }
}