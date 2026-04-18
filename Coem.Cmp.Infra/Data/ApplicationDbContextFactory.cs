using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Coem.Cmp.Infra.Data
{
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            // Se mantiene la cadena de conexión configurada para el entorno de base de datos
            var connectionString = "Server=tcp:dbcmp.database.windows.net,1433;Initial Catalog=DB_CMP;Persist Security Info=False;User ID=usradmin;Password=Esteban*10;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

            optionsBuilder.UseSqlServer(connectionString);

            // Al instanciar para diseño (migraciones), pasamos null al ITenantContext.
            // Esto garantiza que el Scope sea "Global" y la migración no sea filtrada.
            return new ApplicationDbContext(optionsBuilder.Options, null);
        }
    }
}