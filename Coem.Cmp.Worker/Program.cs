using Azure.Storage.Blobs; // Necesario para DataProtection
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

// Usamos el builder moderno para .NET 10
var builder = FunctionsApplication.CreateBuilder(args);

// 1. Configuración del Host del Worker
builder.ConfigureFunctionsWebApplication();

// 2. Telemetría y Monitoreo (Separado para evitar errores de extensión)
builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.ConfigureFunctionsApplicationInsights();

// 3. Inyección de la Base de Datos (Resiliente)
var connectionString = Environment.GetEnvironmentVariable("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("La cadena de conexión 'DefaultConnection' no está configurada en las variables de entorno.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 4. Inyección de dependencias y Clientes HTTP
builder.Services.AddHttpClient();

// 5. Configuración Maestra de Data Protection (Sincronizada con el Web)
// Esto es vital para que el CMP pueda leer las credenciales de Azure Direct
var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
var containerName = "keys";
var blobName = "keys.xml";

if (!string.IsNullOrEmpty(storageConnectionString))
{
    builder.Services.AddDataProtection()
        .PersistKeysToAzureBlobStorage(storageConnectionString, containerName, blobName)
        .SetApplicationName("CoemCmp");
}

// 6. El Cerebro Financiero del CMP
builder.Services.AddScoped<IAzureDirectBillingService, AzureDirectBillingService>();
builder.Services.AddScoped<IPartnerCenterSyncService, PartnerCenterSyncService>();

// Ejecución
builder.Build().Run();