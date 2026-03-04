namespace Coem.Cmp.Core.Entities
{
    public abstract class UsageRecordBase
    {
        public long Id { get; set; }
        public Guid SubscriptionId { get; set; }
        public DateTime UsageDate { get; set; }
        public string ProductName { get; set; }
        public string MeterCategory { get; set; }
        public decimal Quantity { get; set; }

        // --- MOTOR FINANCIERO ZENITH ---
        public decimal EstimatedCost { get; set; }     // Costo real de Microsoft (Raw Cost)
        public decimal MarkupPercentage { get; set; }  // Margen aplicado (ej: 0.15 para 15%)
        public decimal BilledCost { get; set; }        // Costo final que verá el cliente (Costo + Margen)

        public string Currency { get; set; }
        public string ProviderSource { get; set; }     // Sello de auditoría ("PartnerCenter" o "BYOT_EA")
    }
}