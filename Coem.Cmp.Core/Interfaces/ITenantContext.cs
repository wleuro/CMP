namespace Coem.Cmp.Core.Interfaces
{
    public interface ITenantContext
    {
        // El ID interno de nuestra base de datos para el cliente
        int? CurrentTenantId { get; }

        // El país del usuario (Vital para el rol Comercial)
        string? CurrentCountry { get; }

        // El rol Zenith asignado (TAM, Comercial, etc.)
        string? Role { get; }

        // El alcance de visibilidad (Global, Regional, SingleTenant)
        string Scope { get; }

        // Atajo booleano para lógica que solo aplica a empleados de Coem
        bool IsCoemStaff { get; }
    }
}