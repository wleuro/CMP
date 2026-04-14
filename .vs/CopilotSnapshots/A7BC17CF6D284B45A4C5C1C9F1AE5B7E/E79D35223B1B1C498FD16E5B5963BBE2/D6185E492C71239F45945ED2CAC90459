namespace Coem.Cmp.Core.Entities
{
    public class AzureDirectCredential
    {
        public int Id { get; set; }

        // Identificador visual para que sepas qué entorno es
        public string Alias { get; set; }

        // Credenciales del Service Principal
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; } // Deberá ir cifrado

        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}