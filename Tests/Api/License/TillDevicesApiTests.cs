using System.Net;
using System.Text.Json;
using Application.DTOs.Licensing;
using Application.IServices.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.License;

/// <summary>
/// The registered tills behind a multi-seat licence, over HTTP: who may manage them, pairing with a one-time code, and that a PC other
/// than the store server cannot sell until it is paired. The host holds a licence that covers 3 PCs (the server plus 2 tills) and a test
/// header (<c>X-Test-Remote</c>) lets a request pretend to come from another PC on the shop network: the in-process host has no
/// real socket, and a request without an address counts as the server's own PC.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TillDevicesApiTests : CatalogApiTestBase, IAsyncLifetime
{
    private const string RemoteHeader = "X-Test-Remote";
    private const string TillIp = "192.168.1.20";

    private static WebApplicationFactory<Program>? _host;
    private static readonly object HostGate = new();

    public TillDevicesApiTests(ApiFactory factory) : base(factory)
    {
    }

    private WebApplicationFactory<Program> Host
    {
        get
        {
            lock (HostGate)
            {
                return _host ??= Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ILicenseService>();
                    services.AddSingleton<ILicenseService, ThreePcLicense>();
                    services.AddSingleton<IStartupFilter, FakeRemoteAddress>();
                }));
            }
        }
    }

    private sealed class ThreePcLicense : ILicenseService
    {
        public LicenseState GetState() => new()
        {
            Status = LicenseStatus.Active,
            MachineId = "TEST-TEST-TEST-TEST",
            BuildDate = DateOnly.FromDateTime(DateTime.Today),
            Seats = 3,
        };

        public LicenseActivationResult Activate(string licenseKeyText) => throw new NotSupportedException();
        public void EnsureSalesAllowed() { }
    }

    private sealed class FakeRemoteAddress : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteHeader, out var ip)) context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                await nextMiddleware();
            });
            next(app);
        };
    }

    private HttpClient Browser() =>
        Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static HttpRequestMessage From(HttpRequestMessage request, string ip)
    {
        request.Headers.Add(RemoteHeader, ip);
        return request;
    }

    private async Task<HttpClient> SignedInAt(string user, string pin)
    {
        var browser = Browser();
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/login", new { username = user, pin }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return browser;
    }

    /// <summary>Starts every test with no tills, whatever the previous test left in the shared database.</summary>
    public async Task InitializeAsync()
    {
        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var seats = await Body(await admin.GetAsync("/api/v1/devices"));
        foreach (var device in seats.GetProperty("devices").EnumerateArray())
            await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/devices/{device.GetProperty("id").GetInt32()}"));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> NewCode(HttpClient admin, string name = "Till 2")
    {
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/devices/pairing-codes", new { name }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Body(response)).GetProperty("code").GetString()!;
    }

    /// <summary>A signed-in cashier on a till PC that is already paired.</summary>
    private async Task<HttpClient> PairedCashierTill(HttpClient admin)
    {
        var till = await SignedInAtRemoteCashier();
        var paired = await till.SendAsync(From(Json(HttpMethod.Post, "/api/v1/devices/pair", new { code = await NewCode(admin) }), TillIp));
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        return till;
    }

    private async Task<HttpClient> SignedInAtRemoteCashier()
    {
        var till = Browser();
        var login = await till.SendAsync(From(Json(HttpMethod.Post, "/api/v1/auth/login",
            new { username = ApiFactory.CashierUser, pin = ApiFactory.CashierPin }), TillIp));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return till;
    }

    /// <summary>A sale attempt with an empty body: it only matters whether the gate lets it through (then validation says 400) or refuses it (403).</summary>
    private static HttpRequestMessage SaleAttempt(string? ip)
    {
        var request = Json(HttpMethod.Post, "/api/v1/pos/sales/checkout", new { });
        return ip is null ? request : From(request, ip);
    }

    // ---- who may manage tills ----------------------------------------------------------------------------------

    [Fact]
    public async Task Managing_tills_needs_a_signed_in_administrator()
    {
        var anonymous = Browser();
        await AssertProblem(await anonymous.GetAsync("/api/v1/devices"), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.SendAsync(Json(HttpMethod.Post, "/api/v1/devices/pairing-codes", new { name = "x" })), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.SendAsync(Json(HttpMethod.Delete, "/api/v1/devices/1")), HttpStatusCode.Unauthorized, "unauthenticated");

        var cashier = await SignedInAt(ApiFactory.CashierUser, ApiFactory.CashierPin);
        await AssertProblem(await cashier.GetAsync("/api/v1/devices"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/devices/pairing-codes", new { name = "x" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Delete, "/api/v1/devices/1")), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task The_administrator_sees_the_package()
    {
        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var seats = await Body(await admin.GetAsync("/api/v1/devices"));

        Assert.Equal(3, seats.GetProperty("seats").GetInt32());
        Assert.Equal(2, seats.GetProperty("tillSeats").GetInt32());
        Assert.Equal(0, seats.GetProperty("used").GetInt32());
        Assert.True(seats.GetProperty("enforced").GetBoolean());
        Assert.Empty(seats.GetProperty("devices").EnumerateArray());
    }

    [Fact]
    public async Task Adding_a_till_returns_a_code_and_holds_a_seat_until_the_package_is_full()
    {
        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var first = await Body(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/devices/pairing-codes", new { name = "Till 2" })));
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", first.GetProperty("code").GetString()!);
        await NewCode(admin, "Till 3");

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/devices/pairing-codes", new { name = "Till 4" })),
            HttpStatusCode.Conflict, "seats_full");

        var seats = await Body(await admin.GetAsync("/api/v1/devices"));
        Assert.Equal(2, seats.GetProperty("used").GetInt32());
        Assert.All(seats.GetProperty("devices").EnumerateArray(), d => Assert.Equal("Pending", d.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task A_till_needs_a_name()
    {
        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/devices/pairing-codes", new { name = "" })),
            HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---- pairing and selling -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_till_that_is_not_paired_cannot_sell_but_the_server_pc_can()
    {
        var till = await SignedInAtRemoteCashier();

        await AssertProblem(await till.SendAsync(SaleAttempt(TillIp)), HttpStatusCode.Forbidden, "device_not_registered");

        var serverPc = await SignedInAt(ApiFactory.CashierUser, ApiFactory.CashierPin);
        Assert.Equal(HttpStatusCode.BadRequest, (await serverPc.SendAsync(SaleAttempt(null))).StatusCode); // past the gate: the empty body is what fails
    }

    [Fact]
    public async Task Pairing_with_a_code_lets_the_till_sell_and_the_cookie_is_script_proof()
    {
        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var till = await SignedInAtRemoteCashier();
        var code = await NewCode(admin, "Back counter");

        var paired = await till.SendAsync(From(Json(HttpMethod.Post, "/api/v1/devices/pair", new { code }), TillIp));

        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        Assert.Equal("Back counter", (await Body(paired)).GetProperty("name").GetString());
        var cookie = Assert.Single(paired.Headers.GetValues("Set-Cookie"), c => c.StartsWith("nexpos_till=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, (await till.SendAsync(SaleAttempt(TillIp))).StatusCode); // through the gate

        var seats = await Body(await admin.GetAsync("/api/v1/devices"));
        var device = Assert.Single(seats.GetProperty("devices").EnumerateArray());
        Assert.Equal("Active", device.GetProperty("state").GetString());
        Assert.Equal("Back counter", device.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_code_works_once_and_a_wrong_one_is_refused()
    {
        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var code = await NewCode(admin);
        var till = Browser();

        await AssertProblem(await till.SendAsync(From(Json(HttpMethod.Post, "/api/v1/devices/pair", new { code = "WRONG-CODE" }), "192.168.1.31")),
            HttpStatusCode.BadRequest, "invalid_pairing_code");
        Assert.Equal(HttpStatusCode.OK, (await till.SendAsync(From(Json(HttpMethod.Post, "/api/v1/devices/pair", new { code }), TillIp))).StatusCode);
        await AssertProblem(await Browser().SendAsync(From(Json(HttpMethod.Post, "/api/v1/devices/pair", new { code }), "192.168.1.32")),
            HttpStatusCode.BadRequest, "invalid_pairing_code");
    }

    [Fact]
    public async Task Guessing_codes_from_one_pc_gets_throttled()
    {
        var till = Browser();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await till.SendAsync(From(Json(HttpMethod.Post, "/api/v1/devices/pair", new { code = "AAAA-AAAA" }), "192.168.1.99"))).StatusCode);

        await AssertProblem(await till.SendAsync(From(Json(HttpMethod.Post, "/api/v1/devices/pair", new { code = "AAAA-AAAA" }), "192.168.1.99")),
            HttpStatusCode.TooManyRequests, "rate_limited");
    }

    [Fact]
    public async Task The_token_can_also_travel_in_a_header()
    {
        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var paired = await Browser().SendAsync(From(Json(HttpMethod.Post, "/api/v1/devices/pair", new { code = await NewCode(admin) }), TillIp));
        var token = System.Net.Http.Headers.NameValueHeaderValue.Parse(
            paired.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("nexpos_till=", StringComparison.Ordinal)).Split(';')[0]).Value!;

        var otherBrowser = await SignedInAtRemoteCashier(); // no cookie of its own
        var request = SaleAttempt(TillIp);
        request.Headers.Add("X-Till-Token", token);

        Assert.Equal(HttpStatusCode.BadRequest, (await otherBrowser.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Removing_a_till_cuts_it_off_and_frees_its_seat()
    {
        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var till = await PairedCashierTill(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await till.SendAsync(SaleAttempt(TillIp))).StatusCode);
        var id = (await Body(await admin.GetAsync("/api/v1/devices"))).GetProperty("devices")[0].GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/devices/{id}"))).StatusCode);

        await AssertProblem(await till.SendAsync(SaleAttempt(TillIp)), HttpStatusCode.Forbidden, "device_not_registered");
        Assert.Equal(0, (await Body(await admin.GetAsync("/api/v1/devices"))).GetProperty("used").GetInt32());
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/devices/{id}")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_paired_till_still_has_the_same_seat_after_signing_in_as_someone_else()
    {
        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var till = await PairedCashierTill(admin);

        // The seat belongs to the PC (its cookie), not to the user: a different person signing in on it still sells.
        await till.SendAsync(From(Json(HttpMethod.Post, "/api/v1/auth/logout"), TillIp));
        var login = await till.SendAsync(From(Json(HttpMethod.Post, "/api/v1/auth/login",
            new { username = ApiFactory.AdminUser, pin = ApiFactory.AdminPin }), TillIp));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await till.SendAsync(SaleAttempt(TillIp))).StatusCode);
    }

    // ---- what this PC is ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_pairing_screen_can_ask_what_this_pc_is_without_signing_in()
    {
        var anonymousServer = Browser();
        var server = await Body(await anonymousServer.GetAsync("/api/v1/devices/me"));
        Assert.True(server.GetProperty("isServer").GetBoolean());
        Assert.Equal("Allowed", server.GetProperty("access").GetString());

        var anonymousTill = Browser();
        var unpaired = await Body(await anonymousTill.SendAsync(From(Json(HttpMethod.Get, "/api/v1/devices/me"), TillIp)));
        Assert.False(unpaired.GetProperty("isServer").GetBoolean());
        Assert.Equal("NotRegistered", unpaired.GetProperty("access").GetString());

        var admin = await SignedInAt(ApiFactory.AdminUser, ApiFactory.AdminPin);
        await anonymousTill.SendAsync(From(Json(HttpMethod.Post, "/api/v1/devices/pair", new { code = await NewCode(admin) }), TillIp));
        var paired = await Body(await anonymousTill.SendAsync(From(Json(HttpMethod.Get, "/api/v1/devices/me"), TillIp)));
        Assert.Equal("Allowed", paired.GetProperty("access").GetString());
    }

    [Fact]
    public async Task A_forged_forwarded_for_header_does_not_make_a_till_look_like_the_server()
    {
        var till = await SignedInAtRemoteCashier();
        var request = SaleAttempt(TillIp);
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");

        await AssertProblem(await till.SendAsync(request), HttpStatusCode.Forbidden, "device_not_registered");
    }
}
