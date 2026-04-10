using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Azure.Functions.Worker; // <--- VITAL PARA EL MOTOR AISLADO
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Web.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
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
            Console.WriteLine("[ALERTA ZENITH] Worker local sin Storage de Azure real. La desencriptación fallará.");
        }

        // 4. Los Motores FinOps
        // ADVERTENCIA TÁCTICA: Si el compilador subraya en rojo 'AzureDirectBillingService', 
        // significa que aún no has creado esa clase. Si es así, simplemente comenta la siguiente línea.
        services.AddScoped<IAzureDirectBillingService, AzureDirectBillingService>();

        services.AddScoped<IPartnerCenterSyncService, PartnerCenterSyncService>();
    })
    .Build();

host.Run();