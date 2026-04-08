using Azure.Identity; // Requerido para Key Vault
using Azure.Storage.Blobs;
using Coem.Cmp.Infra.Data; // Tu contexto de datos
using Coem.Cmp.Web.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore; // Requerido para SQL
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
    throw new InvalidOperationException("CRÍTICO: ConnectionString 'DefaultConnection' no encontrada en Key Vault o appsettings.");
}
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString,
    b => b.MigrationsAssembly("Coem.Cmp.Infra")));

// =========================================================================
// 3. IDENTIDAD Y SEGURIDAD (ESTÁNDAR ZERO TRUST + MICROSOFT GRAPH)
// =========================================================================
// ÚNICA DECLARACIÓN DE AUTENTICACIÓN: Conectada a Entra ID y con permisos para leer la foto.
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi(new string[] { "User.Read" })
    .AddMicrosoftGraph()
    .AddInMemoryTokenCaches();

// --- REGISTRO DEL GUARDIÁN DEL PORTAL ADMIN ---
builder.Services.AddScoped<Coem.Cmp.Web.Security.AdminTenantAuthorizationFilter>();
builder.Services.AddControllersWithViews(options =>
{
    // Bloqueo global nativo
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));

    // --- INYECCIÓN DEL FILTRO ZERO TRUST ---
    options.Filters.AddService<Coem.Cmp.Web.Security.AdminTenantAuthorizationFilter>();
});

builder.Services.AddRazorPages().AddMicrosoftIdentityUI();
builder.Services.AddHttpClient();

// Protección de Datos y Sesiones Distribuidas
var storageConnectionString = builder.Configuration.GetConnectionString("AzureWebJobsStorage");
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(storageConnectionString, "keys", "keys.xml")
    .SetApplicationName("CoemCmp");

// =========================================================================
// 4. REGISTRO DE MOTORES FINOPS E INTERCEPTORES
// =========================================================================
builder.Services.AddScoped<IPartnerCenterSyncService, PartnerCenterSyncService>();
builder.Services.AddScoped<IAzureDirectBillingService, AzureDirectBillingService>();

// Interceptor de Identidad (Sincronización de Nombres desde Entra ID JIT)
builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, Coem.Cmp.Web.Services.ClaimsTransformation>();


// =========================================================================
// EL PUNTO DE NO RETORNO: Cierre del contenedor de servicios
// ¡NADA DE builder.Services DESPUÉS DE ESTA LÍNEA!
// =========================================================================
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

// OBLIGATORIO: Authentication debe ir siempre antes de Authorization
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

        // Ejecuta migraciones e inyecta la matriz de permisos usando el Key Vault
        Coem.Cmp.Infra.Data.DbInitializer.Initialize(context, config);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error crítico al inicializar la base de datos de seguridad.");
    }
}

app.Run();