using Application.DTOs.Licensing;
using Application.IServices.Licensing;
using Application.Services.Licensing;
using Common.Licensing;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Api.Infrastructure;

/// <summary>
/// The whole Web app in-process on a throw-away demo database (users: <c>layla</c>/1234 Admin, <c>sara</c>/1111 Cashier),
/// no licence file (so selling is stopped, whatever the build configuration), and a fake controller that exercises each
/// API convention. Never touches <c>nexpos.db</c>. The plan is Pro (every page open) unless the host comes from <see cref="Basic"/>:
/// the real licence service still decides the status, only the plan is set.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminUser = "layla";
    public const string AdminPin = "1234";
    public const string CashierUser = "sara";
    public const string CashierPin = "1111";

    private readonly int _loginLimitPerMinute;
    private LicensePlan _plan = LicensePlan.Pro;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "nexpos-api-tests", Guid.NewGuid().ToString("N"));

    public ApiFactory() : this(100_000)
    {
    }

    private ApiFactory(int loginLimitPerMinute) => _loginLimitPerMinute = loginLimitPerMinute;

    /// <summary>A host with a tight sign-in throttle (the shipped one is 10 a minute); the default host is effectively unthrottled so tests can sign in freely.</summary>
    public static ApiFactory WithLoginLimit(int perMinute) => new(perMinute);

    /// <summary>A shop on the Basic plan: Pro pages locked, at most 3 active users.</summary>
    public static ApiFactory Basic() => new(100_000) { _plan = LicensePlan.Basic };

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // The app reads these when it starts; the factories of one run are created one after another (single xUnit collection).
        Directory.CreateDirectory(_folder);
        Environment.SetEnvironmentVariable("NEXPOS_DB_PATH", Path.Combine(_folder, "test.db"));
        Environment.SetEnvironmentVariable("NEXPOS_LOG_DIR", Path.Combine(_folder, "logs"));
        Environment.SetEnvironmentVariable("NEXPOS_LICENSE_PATH", Path.Combine(_folder, "no-license.nxl"));
        Environment.SetEnvironmentVariable("NEXPOS_LICENSE_ENFORCE", "1");
        Environment.SetEnvironmentVariable("NEXPOS_SEED_DEMO", "1");
        Environment.SetEnvironmentVariable("NEXPOS_DEMO", "1");
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:SecretKey", "api-tests-only-secret-key-0123456789abcdef");
        builder.UseSetting("IpRateLimiting:GeneralRules:0:Limit", _loginLimitPerMinute.ToString());

        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(FakeApiController).Assembly);

            var plan = _plan;
            var real = services.Single(d => d.ServiceType == typeof(ILicenseService));
            services.Remove(real);
            services.AddSingleton<ILicenseService>(sp =>
                new PlanOverride((ILicenseService)ActivatorUtilities.CreateInstance(sp, real.ImplementationType ?? typeof(LicenseService)), plan));
        });
    }

    /// <summary>A browser: keeps the cookie, never follows redirects (a redirect to the login page is itself a finding).</summary>
    public HttpClient CreateBrowser()
        => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { /* SQLite may still hold a handle for a moment; a leftover temp folder is harmless */ }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>The real licence service, with the plan the test host asked for.</summary>
internal sealed class PlanOverride(ILicenseService inner, LicensePlan plan) : ILicenseService
{
    public LicenseState GetState() => inner.GetState() with { Plan = plan };
    public LicenseActivationResult Activate(string licenseKeyText) => inner.Activate(licenseKeyText);
    public void EnsureSalesAllowed() => inner.EnsureSalesAllowed();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    /// <summary>One collection, so API tests never run beside each other: the host reads process-wide environment variables.</summary>
    public const string Name = "Api";
}
