using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Azure.Identity; // Requerido para Key Vault
using Microsoft.EntityFrameworkCore; // Requerido para SQL
using Coem.Cmp.Infra.Data; // Tu contexto de datos (ajusta el namespace si es diferente)

var builder = WebApplication.CreateBuilder(args); // Corregido: Faltaba el punto y coma

// =========================================================================
// 1. GOBERNANZA DE SECRETOS (EVIDENCIA CONTROL 3B.2.5)
// =========================================================================
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    // El auditor verá que inyectas secretos en tiempo de ejecución. Cero contraseñas expuestas.
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential());
}

// =========================================================================
// 2. BASE DE DATOS (LA MEMORIA FINANCIERA)
// =========================================================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Fail-fast: Si la bóveda no responde o falta el secreto, que la app muera aquí.
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("CRÍTICO: ConnectionString 'DefaultConnection' no encontrada en Key Vault o appsettings.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString,
    b => b.MigrationsAssembly("Coem.Cmp.Infra"))); // Forzamos a que las migraciones vivan en Web

// =========================================================================
// 3. IDENTIDAD Y SEGURIDAD (EVIDENCIA CONTROL 3B.2.3)
// =========================================================================
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddControllersWithViews(options =>
{
    // Excelente: Bloqueo global. Nadie entra a la app sin pasar por Entra ID.
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});

builder.Services.AddRazorPages()
    .AddMicrosoftIdentityUI();

builder.Services.AddHttpClient(); // Vital para no agotar los sockets del servidor
builder.Services.AddScoped<Coem.Cmp.Web.Services.IPartnerCenterSyncService, Coem.Cmp.Web.Services.PartnerCenterSyncService>();

var app = builder.Build();

// =========================================================================
// 4. PIPELINE HTTP
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
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();