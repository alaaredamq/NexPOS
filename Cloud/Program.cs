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

// Server-only settings (admin login, public address, database): a file the publish never contains or overwrites, read whatever the
// environment is called. It wins over appsettings.json; see Cloud/deploy/cloud.settings.json.example.
builder.Configuration.AddJsonFile("cloud.settings.json", optional: true, reloadOnChange: false);

builder.Services.AddOptions<CloudOptions>().BindConfiguration("Cloud");
builder.Services.AddSingleton(TimeProvider.System);

// Control database: SQL Server when Cloud:ControlConnectionString is set, otherwise SQLite in DataRoot.
var controlConnection = builder.Configuration["Cloud:ControlConnectionString"];
if (!string.IsNullOrWhiteSpace(controlConnection))
{
    builder.Services.AddDbContext<ControlDbContext, SqlServerControlDbContext>(options =>
        options.UseSqlServer(controlConnection, sql => sql.EnableRetryOnFailure()));
}
else
{
    builder.Services.AddDbContext<ControlDbContext>((sp, options) =>
    {
        var root = Path.GetFullPath(sp.GetRequiredService<IOptions<CloudOptions>>().Value.DataRoot);
        Directory.CreateDirectory(root);
        options.UseSqlite($"Data Source={Path.Combine(root, "control.db")}");
    });
}

builder.Services.AddSingleton(_ => MirrorStore.BuildSchema());
builder.Services.AddSingleton<MirrorApplier>();
builder.Services.AddSingleton<MirrorStore>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<Cloud.Api.MenuOrderThrottle>();
builder.Services.AddSingleton<CommandBroker>();
builder.Services.AddSingleton<MirrorReader>();
builder.Services.AddSingleton<PortalFormat>();
builder.Services.AddScoped<EnrollmentService>();
builder.Services.AddScoped<SiteRemoval>();
builder.Services.AddScoped<FinanceBook>();
builder.Services.AddScoped<AdminOperations>();
builder.Services.AddScoped<AdminSignIn>();
builder.Services.AddScoped<OverviewBuilder>();
builder.Services.AddScoped<FinanceDashboard>();
builder.Services.AddSingleton<LicenseSigner>();
builder.Services.AddScoped<LicenseBook>();
builder.Services.AddSingleton<MenuImages>();
builder.Services.AddScoped<MenuCatalogMerge>();

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
        options.LoginPath = PublicSite.AdminLogin;
        options.AccessDeniedPath = PublicSite.AdminLogin;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    // The admin app's bearer tokens (/api/admin/v1). Only that API asks for this scheme; the portal pages stay on the cookie.
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AdminTokenHandler>(AdminTokenHandler.SchemeName, null);

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
    // Public pages (a shop's online menu at /m/{slug}) need no sign-in and stay out of /admin.
    options.Conventions.AllowAnonymousToFolder("/Public");
    // The provider portal lives under /admin; "/" is the public landing page (see PublicSite).
    options.Conventions.Add(new PublicSite.AdminPrefixConvention());
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
    await FinanceRules.RenameOldWhishAsync(services.GetRequiredService<ControlDbContext>());
    await AdminSeeder.EnsureAsync(
        services.GetRequiredService<ControlDbContext>(),
        services.GetRequiredService<IOptions<CloudOptions>>().Value,
        services.GetRequiredService<TimeProvider>(),
        app.Logger);
}

app.UseForwardedHeaders();
app.UseCanonicalAddress();
app.UseLandingTemplateGuard();
app.UseOldPortalRedirects();
app.UseStaticFiles(new StaticFileOptions
{
    // Pages, scripts and styles are not versioned in their URLs, so without this a browser keeps running the copy it saved
    // for hours or days after a publish. "no-cache" still lets it keep the file; it just asks first and gets a cheap 304 if unchanged.
    OnPrepareResponse = ctx =>
    {
        var ext = Path.GetExtension(ctx.File.Name);
        if (ext is ".html" or ".js" or ".css" or ".json" or ".webmanifest")
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    },
});
// "/" is the public Coduo POS landing page for everyone; the portal is at /admin (see PublicSite).
app.UseLandingPage();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Says which database the control plane is on (never the connection string), so "did my settings file get read?" has an answer.
var controlDb = string.IsNullOrWhiteSpace(controlConnection) ? "sqlite" : "sqlserver";
app.Logger.LogInformation("Control database: {Provider}", controlDb);
var settingsFileFound = File.Exists(Path.Combine(app.Environment.ContentRootPath, "cloud.settings.json"));
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", controlDb, settingsFileFound })).AllowAnonymous();
// The owner's website (static files in wwwroot/owner): public pages that sign in through /owner/v1 and decrypt in the browser.
app.MapGet("/owner", () => Results.Redirect("/owner/index.html")).AllowAnonymous();
app.MapSyncApi();
app.MapOwnerApi();
app.MapEnrollApi();
app.MapMenuApi();
app.MapPublicSite();
app.MapAdminApi();
// The provider console: a React app (source in cloud-console/, built into wwwroot/console). Its files are public on purpose: they
// hold no data. Everything it shows comes from the admin API above, which needs a sign-in token. Any /console/... address the app
// owns (a client page, Finance) is answered with its index.html, never cached, so a publish reaches the browser at once.
// "/console" and "/console/" both match (an empty catch-all), so there is no redirect to loop on.
app.MapFallbackToFile("/console/{**path:nonfile}", "console/index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
}).AllowAnonymous();
app.MapRazorPages();

app.Run();

/// <summary>Lets WebApplicationFactory (tests) find the entry point.</summary>
public partial class Program;
