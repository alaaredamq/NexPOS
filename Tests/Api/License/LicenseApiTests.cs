using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Common.Licensing;
using Microsoft.Extensions.Localization;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Tests.Licensing;
using Web;
using Web.Api.License;
using Xunit;

namespace Tests.Api.License;

/// <summary>
/// Licence API (track A4): who may open the licence screen, that it stays reachable while selling is stopped (the test host has no licence file,
/// so it always is), how a refused key is reported, and the mapping of a real <see cref="Application.Services.Licensing.LicenseService"/> state - including
/// a successful activation - to the DTO. The host cannot be given a valid key (it holds the real public key), so activation success is tested at the service level.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LicenseApiTests : CatalogApiTestBase
{
    public LicenseApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static HttpRequestMessage WithLanguage(HttpRequestMessage request, string language)
    {
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        return request;
    }

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_the_licence_screen_answers_401()
    {
        var anonymous = Factory.CreateBrowser();

        await AssertProblem(await anonymous.GetAsync("/api/v1/license"), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.SendAsync(Json(HttpMethod.Post, "/api/v1/license/activate", new { licenseKey = "x" })), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_cannot_open_or_change_the_licence()
    {
        var cashier = await Cashier();

        await AssertProblem(await cashier.GetAsync("/api/v1/license"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/license/activate", new { licenseKey = "x" })), HttpStatusCode.Forbidden, "forbidden");
    }

    // ---- reading it while selling is stopped ---------------------------------------------------------------------

    [Fact]
    public async Task An_administrator_can_read_the_licence_although_selling_is_stopped()
    {
        var admin = await Admin();
        Assert.False((await Body(await admin.GetAsync("/api/v1/session"))).GetProperty("license").GetProperty("allowsSales").GetBoolean());

        var response = await admin.GetAsync("/api/v1/license");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var license = await Body(response);
        Assert.Equal("NotActivated", license.GetProperty("status").GetString());
        Assert.Equal("danger", license.GetProperty("severity").GetString());
        Assert.False(license.GetProperty("allowsSales").GetBoolean());
        Assert.False(license.GetProperty("showsWarning").GetBoolean());
        Assert.False(license.GetProperty("isTimeLimited").GetBoolean());
        Assert.Matches(new Regex("^[A-Z0-9]{5}(-[A-Z0-9]{5}){3}$"), license.GetProperty("machineId").GetString());
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}$"), license.GetProperty("buildDate").GetString());
        Assert.False(string.IsNullOrWhiteSpace(license.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(license.GetProperty("message").GetString()));
        Assert.Equal(JsonValueKind.Null, license.GetProperty("kind").ValueKind);
        Assert.Equal(JsonValueKind.Null, license.GetProperty("customer").ValueKind);
    }

    [Fact]
    public async Task The_title_and_message_follow_the_language_of_the_request()
    {
        var admin = await Admin();

        var english = await Body(await admin.GetAsync("/api/v1/license"));
        var arabic = await Body(await admin.SendAsync(WithLanguage(new HttpRequestMessage(HttpMethod.Get, "/api/v1/license"), "ar")));

        Assert.NotEqual(english.GetProperty("message").GetString(), arabic.GetProperty("message").GetString());
        Assert.Matches(new Regex(@"\p{IsArabic}"), arabic.GetProperty("message").GetString());
        Assert.Equal(english.GetProperty("machineId").GetString(), arabic.GetProperty("machineId").GetString());
    }

    // ---- activation --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_refused_key_lands_on_the_field_and_changes_nothing()
    {
        var admin = await Admin();
        var before = await Body(await admin.GetAsync("/api/v1/license"));

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/license/activate", new { licenseKey = "not a licence key" }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        var message = (await Body(response)).GetProperty("errors").GetProperty("licenseKey")[0].GetString();
        Assert.Contains("incomplete", message);
        Assert.Equal(before.GetProperty("status").GetString(), (await Body(await admin.GetAsync("/api/v1/license"))).GetProperty("status").GetString());

        var arabic = await admin.SendAsync(WithLanguage(Json(HttpMethod.Post, "/api/v1/license/activate", new { licenseKey = "not a licence key" }), "ar"));
        Assert.Matches(new Regex(@"\p{IsArabic}"), (await Body(arabic)).GetProperty("errors").GetProperty("licenseKey")[0].GetString());
    }

    [Fact]
    public async Task A_missing_or_oversized_key_is_refused_before_it_is_looked_at()
    {
        var admin = await Admin();

        foreach (var key in new[] { "", new string('A', 4001) })
        {
            var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/license/activate", new { licenseKey = key }));
            await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
            Assert.True((await Body(response)).GetProperty("errors").TryGetProperty("licenseKey", out _));
        }

        // The 16 KB cap on the request body ([RequestSizeLimit]) is Kestrel's; the in-process test server does not enforce it, so only the field limit shows here.
    }

    // ---- the mapping, with a real LicenseService ----------------------------------------------------------------

    private sealed class KeyLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    private static (LicenseApiService Api, LicenseTestIssuer Issuer, InMemoryLicenseStorage Storage) ApiOver(DateOnly today)
    {
        var issuer = new LicenseTestIssuer();
        var storage = new InMemoryLicenseStorage();
        var service = LicenseServiceFactory.Create(issuer, storage, new FakeClock(today));
        return (new LicenseApiService(service, new KeyLocalizer()), issuer, storage);
    }

    [Fact]
    public void Activating_a_valid_trial_key_returns_the_new_state_with_its_dates()
    {
        var today = new DateOnly(2026, 11, 1);
        var (api, issuer, storage) = ApiOver(today);
        using var _ = issuer;
        Assert.Equal("NotActivated", api.Get().Status);

        var key = issuer.Issue(LicenseKind.Trial, TestMachines.ShopPc, today, today.AddDays(30), "Corner Shop", 0xCAFE0001);
        var activated = api.Activate(key);

        Assert.Equal("Active", activated.Status);
        Assert.Equal("success", activated.Severity);
        Assert.True(activated.AllowsSales);
        Assert.Equal("Trial", activated.Kind);
        Assert.True(activated.IsTimeLimited);
        Assert.Equal("Corner Shop", activated.Customer);
        Assert.Equal("2026-11-01", activated.IssuedOn);
        Assert.Equal("2026-12-01", activated.ValidUntil);
        Assert.Equal("2026-12-08", activated.SalesStopAfter);
        Assert.NotNull(storage.Text);
        Assert.Equal(activated.Status, api.Get().Status);
        Assert.Equal(TestMachines.ShopPc.ToString(), activated.MachineId);
    }

    [Fact]
    public void A_full_licence_is_not_time_limited_and_a_key_for_another_pc_is_refused_in_words()
    {
        var today = new DateOnly(2026, 11, 1);
        var (api, issuer, storage) = ApiOver(today);
        using var _ = issuer;

        var wrongPc = Assert.Throws<LicenseActivationException>(
            () => api.Activate(issuer.Issue(LicenseKind.Full, TestMachines.OtherPc, today, today.AddYears(1))));
        Assert.Equal("License_Error_WrongMachine", wrongPc.Message);
        Assert.Null(storage.Text);

        var full = api.Activate(issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, today, today.AddYears(1)));
        Assert.Equal("Full", full.Kind);
        Assert.False(full.IsTimeLimited);
        Assert.Equal("2027-11-01", full.ValidUntil);
        Assert.Null(full.SalesStopAfter);

        var older = Assert.Throws<LicenseActivationException>(
            () => api.Activate(issuer.Issue(LicenseKind.Trial, TestMachines.ShopPc, today.AddDays(-5), today.AddDays(10), "Corner Shop", 0x0000_0001)));
        Assert.Equal("License_Error_OlderThanCurrent", older.Message);
    }

    [Fact]
    public void A_licence_near_its_end_carries_the_warning_and_the_countdown()
    {
        var today = new DateOnly(2026, 11, 1);
        var (api, issuer, _) = ApiOver(today);
        using var _ = issuer;

        var expiring = api.Activate(issuer.Issue(LicenseKind.Provisional, TestMachines.ShopPc, today.AddDays(-20), today.AddDays(3)));
        Assert.Equal("ExpiringSoon", expiring.Status);
        Assert.Equal("warning", expiring.Severity);
        Assert.True(expiring.ShowsWarning);
        Assert.True(expiring.AllowsSales);
        Assert.Equal(3, expiring.DaysLeft);
    }
}
