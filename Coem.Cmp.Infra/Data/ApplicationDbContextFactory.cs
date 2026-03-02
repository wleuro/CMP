using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Coem.Cmp.Infra.Data
{
    // Esta clase SOLO la usan las herramientas de línea de comandos (dotnet ef).
    // Tu aplicación web en producción jamás la tocará, manteniendo el Key Vault intacto.
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            // ⚠️ ACCIÓN REQUERIDA: Pega tu cadena de conexión real de Azure SQL aquí.
            // Ejemplo: "Server=tcp:tuservidor.database.windows.net,1433;Initial Catalog=tudb;..."
            var connectionString = "Server=tcp:dbcmp.database.windows.net,1433;Initial Catalog=DB_CMP;Persist Security Info=False;User ID=usradmin;Password=Esteban*10;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

            optionsBuilder.UseSqlServer(connectionString);

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}