using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Web.Services;
using System;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // 1. Inyectar la Base de Datos
        // En Azure Functions, leemos las variables de entorno, no el appsettings.json
        var connectionString = Environment.GetEnvironmentVariable("DefaultConnection");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString));

        // 2. Inyectar HttpClient y Data Protection (Requeridos por tu servicio)
        services.AddHttpClient();
        services.AddDataProtection();

        // 3. Inyectar el Servicio Financiero que vive en tu proyecto Web
        services.AddScoped<IAzureDirectBillingService, AzureDirectBillingService>();
    })
    .Build();

host.Run();