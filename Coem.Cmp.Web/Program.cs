using Azure.Identity;
using Coem.Cmp.Infra.Data;
using Coem.Cmp.Web.Services;
using Coem.Cmp.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using System.Security.Claims; // Necesario para ClaimTypes

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. GOBERNANZA DE SECRETOS (KEY VAULT)
// =========================================================================
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential());
}

// =========================================================================
// 2. IDENTIDAD Y SEGURIDAD (ESTÁNDAR ZERO TRUST)
// =========================================================================
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi(new[] { "User.Read" })
    .AddMicrosoftGraph()
    .AddInMemoryTokenCaches();

// 🛡️ EL FIX MAESTRO: Mapeo de Roles para que AuthorizeAsync funcione
builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// =========================================================================
// 3. BASE DE DATOS (ESTRUCTURA NUCLEAR)
// =========================================================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("CRÍTICO: ConnectionString no encontrada.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString,
    b => b.MigrationsAssembly("Coem.Cmp.Infra")));

// =========================================================================
// 4. POLÍTICAS DE AUTORIZACIÓN (MAPEO DE ROLES)
// =========================================================================
builder.Services.AddAuthorization(options =>
{
    // Ahora que RoleClaimType está configurado, RequireRole funcionará
    options.AddPolicy("TenantSetup", policy => policy.RequireRole("GlobalAdmin"));
    options.AddPolicy("ViewBaseCosts", policy => policy.RequireRole("GlobalAdmin", "Operaciones"));
    options.AddPolicy("ManageMarkups", policy => policy.RequireRole("GlobalAdmin", "Comercial"));
    options.AddPolicy("TechOps", policy => policy.RequireRole("GlobalAdmin", "Operaciones", "Soporte TI"));
});

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
builder.Services.AddHttpClient();

// Protección de Datos
var storageConnectionString = builder.Configuration.GetConnectionString("AzureWebJobsStorage");
if (!string.IsNullOrEmpty(storageConnectionString))
{
    builder.Services.AddDataProtection()
        .PersistKeysToAzureBlobStorage(storageConnectionString, "keys", "keys.xml")
        .SetApplicationName("CoemCmp");
}

// =========================================================================
// 5. REGISTRO DE SERVICIOS Y TRANSFORMACIÓN
// =========================================================================
builder.Services.AddScoped<IPartnerCenterSyncService, PartnerCenterSyncService>();
builder.Services.AddScoped<IAzureDirectBillingService, AzureDirectBillingService>();
builder.Services.AddScoped<IClaimsTransformation, ClaimsTransformation>();

var app = builder.Build();

// =========================================================================
// 6. PIPELINE HTTP
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
// 7. INICIALIZACIÓN (SEED)
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