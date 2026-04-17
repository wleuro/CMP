namespace Coem.Cmp.Core.Entities
{
    public class CategoryDefinition
    {
        public int Id { get; set; }
        public required string Code { get; set; } // Ej: M365
        public required string Name { get; set; } // Ej: Microsoft 365 y Colaboración
    }
}