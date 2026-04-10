using Azure.Identity;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Web.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. GOBERNANZA DE SECRETOS (EVIDENCIA CONTROL 3B.2.5)
// =========================================================================
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential());
}

// =========================================================================
// 2. BASE DE DATOS (LA MEMORIA FINANCIERA)
// =========================================================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("CRÍTICO: ConnectionString 'DefaultConnection' no encontrada.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString,
    b => b.MigrationsAssembly("Coem.Cmp.Infra")));

// =========================================================================
// 3. IDENTIDAD Y SEGURIDAD (ESTÁNDAR ZERO TRUST + MICROSOFT GRAPH)
// =========================================================================
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    // AQUÍ ESTABA EL ERROR MORTAL. SE REMOVIÓ PartnerBilling.Read.All.
    .EnableTokenAcquisitionToCallDownstreamApi(new string[] { "User.Read" })
    .AddMicrosoftGraph()
    .AddInMemoryTokenCaches();

builder.Services.AddScoped<Coem.Cmp.Web.Security.AdminTenantAuthorizationFilter>();

builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
    options.Filters.AddService<Coem.Cmp.Web.Security.AdminTenantAuthorizationFilter>();
});

builder.Services.AddRazorPages().AddMicrosoftIdentityUI();

// OBLIGATORIO para el motor de sincronización asíncrono
builder.Services.AddHttpClient();

// Protección de Datos y Sesiones Distribuidas
var storageConnectionString = builder.Configuration.GetConnectionString("AzureWebJobsStorage");
if (!string.IsNullOrEmpty(storageConnectionString))
{
    builder.Services.AddDataProtection()
        .PersistKeysToAzureBlobStorage(storageConnectionString, "keys", "keys.xml")
        .SetApplicationName("CoemCmp");
}

// =========================================================================
// 4. REGISTRO DE MOTORES FINOPS E INTERCEPTORES
// =========================================================================
// Inyección del servicio actualizado con ILogger y Graph Support
builder.Services.AddScoped<IPartnerCenterSyncService, PartnerCenterSyncService>();
builder.Services.AddScoped<IAzureDirectBillingService, AzureDirectBillingService>();

builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, Coem.Cmp.Web.Services.ClaimsTransformation>();

var app = builder.Build();

// =========================================================================
// 5. PIPELINE HTTP (Middleware)
// =========================================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

// =========================================================================
// 6. INYECCIÓN DE DATOS INICIALES (SEMILLA DE SEGURIDAD)
// =========================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var config = services.GetRequiredService<IConfiguration>();
        Coem.Cmp.Infra.Data.DbInitializer.Initialize(context, config);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error crítico al inicializar la base de datos.");
    }
}

app.Run();