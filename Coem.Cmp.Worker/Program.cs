using Coem.Cmp.Infra.Data;
using Coem.Cmp.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// 1. Telemetría y Monitoreo
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// 2. Inyección de la Base de Datos (Lee de variables de entorno, no de appsettings)
var connectionString = Environment.GetEnvironmentVariable("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 3. Inyección de dependencias críticas
builder.Services.AddHttpClient();
builder.Services.AddDataProtection(); // Vital para desencriptar los secretos de Azure Direct

// 4. El Cerebro Financiero
builder.Services.AddScoped<IAzureDirectBillingService, AzureDirectBillingService>();
// (Aquí también inyectaremos el PartnerCenterSyncService más adelante)
builder.Services.AddScoped<IPartnerCenterSyncService, PartnerCenterSyncService>();

// Worker - Program.cs
var workerConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
var workerContainer = "keys";
var workerBlob = "keys.xml";

builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(workerConnectionString, workerContainer, workerBlob)
    .SetApplicationName("CoemCmp"); // DEBE SER IDÉNTICO AL DEL WEB

builder.Build().Run();