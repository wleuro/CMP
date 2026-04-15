using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Azure.Functions.Worker;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Web.Services;
using Serilog; // VITAL: El cerebro de telemetría estructurada

// CONFIGURACIÓN DE SERILOG (El sumidero de archivos)
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "C:\\Projects\\CMP\\Logs\\WorkerLogs-.txt", // Cambia esto si quieres otra ruta
        rollingInterval: RollingInterval.Day, // Crea un archivo nuevo cada medianoche
        retainedFileCountLimit: 7, // Solo guarda los últimos 7 días. Eficiencia de almacenamiento.
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Arrancando el Host del Worker...");

    var host = new HostBuilder()
        .ConfigureFunctionsWorkerDefaults()
        .UseSerilog() // VITAL: Secuestra el motor de logs deficiente de Azure y pone a Serilog al mando
        .ConfigureServices((context, services) =>
        {
            var config = context.Configuration;

            // 1. LECTURA DE CONFIGURACIÓN ROBUSTA
            var connectionString = config.GetConnectionString("DefaultConnection")
                                   ?? Environment.GetEnvironmentVariable("DefaultConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("CRÍTICO: La cadena de conexión 'DefaultConnection' no existe.");
            }

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));

            // 2. Clientes HTTP
            services.AddHttpClient();

            // 3. ANILLO CRIPTOGRÁFICO MAESTRO
            var storageConnectionString = config.GetValue<string>("AzureWebJobsStorage")
                                          ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            if (!string.IsNullOrEmpty(storageConnectionString) && storageConnectionString != "UseDevelopmentStorage=true")
            {
                services.AddDataProtection()
                    .PersistKeysToAzureBlobStorage(storageConnectionString, "keys", "keys.xml")
                    .SetApplicationName("CoemCmp");
            }
            else
            {
                // Reemplazamos tu Console.WriteLine amateur por un Warning oficial estructurado
                Log.Warning("Worker local sin Storage de Azure real. La desencriptación fallará.");
            }

            // 4. Los Motores FinOps
            services.AddScoped<IAzureDirectBillingService, AzureDirectBillingService>();
            services.AddScoped<IPartnerCenterSyncService, PartnerCenterSyncService>();
        })
        .Build();

    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "El Worker colapsó de manera catastrófica durante el arranque.");
}
finally
{
    Log.CloseAndFlush(); // Fuerza el guardado del buffer al disco duro antes de que el proceso muera
}