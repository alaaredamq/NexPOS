using Cloud.Api;
using Cloud.Data;
using Cloud.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sync;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<CloudOptions>().BindConfiguration("Cloud");
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<ControlDbContext>((sp, options) =>
{
    var root = Path.GetFullPath(sp.GetRequiredService<IOptions<CloudOptions>>().Value.DataRoot);
    Directory.CreateDirectory(root);
    options.UseSqlite($"Data Source={Path.Combine(root, "control.db")}");
});

builder.Services.AddSingleton(_ => MirrorStore.BuildSchema());
builder.Services.AddSingleton<MirrorApplier>();
builder.Services.AddSingleton<MirrorStore>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<CommandBroker>();
builder.Services.AddSingleton<MirrorReader>();
builder.Services.AddSingleton<PortalFormat>();
builder.Services.AddScoped<EnrollmentService>();

// Keep the cookie/TempData encryption keys on the data volume, or every redeploy would sign the admin out.
builder.Services.AddDataProtection().SetApplicationName("NexPOS.Cloud");
builder.Services.AddOptions<KeyManagementOptions>().Configure<IOptions<CloudOptions>, ILoggerFactory>((keys, cloud, loggers) =>
    keys.XmlRepository = new FileSystemXmlRepository(
        new DirectoryInfo(Path.Combine(Path.GetFullPath(cloud.Value.DataRoot), "keys")), loggers));

// Tills push whole batches; allow room for a first full snapshot of a large shop.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024 * 1024);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "nexpos.cloud.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Behind a TLS-terminating proxy the forwarded scheme is https, so Always is right in production.
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .PostConfigure<IWebHostEnvironment>((options, env) =>
        options.Cookie.SecurePolicy = env.IsDevelopment() || env.EnvironmentName == "Testing"
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always);

// Everything needs an admin sign-in unless it opts out (login page, health check, the device-token API).
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build());

builder.Services.AddRazorPages(options => {
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Enroll");
});

// Behind Caddy/nginx: trust the proxy's X-Forwarded-* so the login throttle sees real client addresses and cookies see https.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await services.GetRequiredService<ControlDbContext>().Database.MigrateAsync();
    await AdminSeeder.EnsureAsync(
        services.GetRequiredService<ControlDbContext>(),
        services.GetRequiredService<IOptions<CloudOptions>>().Value,
        services.GetRequiredService<TimeProvider>(),
        app.Logger);
}

app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapSyncApi();
app.MapOwnerApi();
app.MapEnrollApi();
app.MapRazorPages();

app.Run();

/// <summary>Lets WebApplicationFactory (tests) find the entry point.</summary>
public partial class Program;
