using System.Net;
using System.Net.Http.Json;
using Sync;
using System.Text.RegularExpressions;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cloud.Tests;

public class AdminPortalTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public AdminPortalTests(CloudFactory cloud) => _cloud = cloud;

    private HttpClient Browser() => _cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient c, string user, string password)
        => HtmlForms.PostAsync(c, "/Account/Login", "/Account/Login", ("Username", user), ("Password", password));

    private async Task<HttpClient> SignedInAsync()
    {
        var c = Browser();
        var response = await LoginAsync(c, CloudFactory.AdminUser, CloudFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return c;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Clients/Create")]
    [InlineData("/Clients/Details/1")]
    [InlineData("/Sites/Details/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public async Task Every_portal_page_requires_sign_in(string url)
    {
        var response = await Browser().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Wrong_password_is_refused_and_right_password_opens_the_portal()
    {
        var c = Browser();
        var wrong = await LoginAsync(c, CloudFactory.AdminUser, "not-the-password");
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Wrong username or password", await wrong.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/")).StatusCode);

        var ok = await LoginAsync(c, CloudFactory.AdminUser, CloudFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_login_even_for_the_right_password()
    {
        using var factory = new CloudFactory();
        var c = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        for (var i = 0; i < 5; i++)
            await LoginAsync(c, CloudFactory.AdminUser, $"guess-{i}");

        var locked = await LoginAsync(c, CloudFactory.AdminUser, CloudFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("Too many failed attempts", await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Open_redirects_through_returnUrl_are_not_followed()
    {
        var c = Browser();
        var html = await c.GetStringAsync("/Account/Login?returnUrl=https%3A%2F%2Fevil.example%2F");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await c.PostAsync("/Account/Login?returnUrl=https%3A%2F%2Fevil.example%2F", new FormUrlEncodedContent(
        [
            new("Username", CloudFactory.AdminUser), new("Password", CloudFactory.AdminPassword), new("__RequestVerificationToken", token),
        ]));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Posting_without_an_antiforgery_token_is_refused()
    {
        var c = await SignedInAsync();
        var response = await c.PostAsync("/Clients/Create", new FormUrlEncodedContent([new("Name", "Sneaky"), new("Months", "1")]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_client_gives_a_ready_connection_link_that_a_till_can_use_once()
    {
        var c = await SignedInAsync();

        // 1. One form: the client, 3 months of Online, and (automatically) the till and its link.
        var created = await HtmlForms.PostAsync(c, "/Clients/Create", "/Clients/Create",
            ("Name", "Hassan Minimarket"), ("Phone", "03 000 000"), ("Months", "3"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var siteUrl = created.Headers.Location!.OriginalString;
        Assert.Matches(@"^/Sites/Details/[0-9a-f-]{36}$", siteUrl);

        // 2. The next screen already shows the link, with copy and WhatsApp.
        var page = await c.GetStringAsync(siteUrl);
        var link = Regex.Match(page, @"http://localhost/enroll/[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}").Value;
        Assert.NotEmpty(link);
        Assert.Contains("https://wa.me/?text=", page);
        Assert.Contains("data-copy=\"#enroll-link\"", page);
        Assert.Contains("Waiting for the till", page);
        Assert.Matches(@"expires on \d{4}-\d{2}-\d{2}\.", page); // the date must be filled in, not blank

        // 3. Shown once: a reload no longer holds it, the status says what is going on.
        Assert.DoesNotContain(link, await c.GetStringAsync(siteUrl));

        // 4. The till redeems it for a credential, and the portal then shows it connected.
        var enrolled = await _cloud.CreateClient().PostAsJsonAsync("/api/enroll",
            new EnrollRequest(link[(link.LastIndexOf('/') + 1)..], "ABCDE-12345-ABCDE-12345", "SHOP-PC"), SyncWire.Json);
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var token = (await enrolled.Content.ReadFromJsonAsync<EnrollResponse>(SyncWire.Json))!.Token;
        Assert.Equal(HttpStatusCode.OK, (await _cloud.DeviceClient(token).GetAsync("/api/sync/ping")).StatusCode);

        var connected = await c.GetStringAsync(siteUrl);
        Assert.Contains("Connected", connected);
        Assert.Contains("ABCDE-12345-ABCDE-12345", connected); // the machine id the till reported
        Assert.DoesNotContain(token, connected);

        // 5. Only a hash of the token is kept.
        var siteId = Guid.Parse(siteUrl[(siteUrl.LastIndexOf('/') + 1)..]);
        var stored = await _cloud.WithControlDbAsync(db => db.DeviceTokens.AsNoTracking().Where(t => t.SiteId == siteId).ToListAsync());
        Assert.Single(stored);
        Assert.Equal(DeviceTokens.Hash(token), stored[0].TokenHash);

        // 6. Disconnect from the portal revokes the credential at once.
        await HtmlForms.PostAsync(c, siteUrl, $"{siteUrl}?handler=Disconnect");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cloud.DeviceClient(token).GetAsync("/api/sync/ping")).StatusCode);
        Assert.Contains("Disconnected", await c.GetStringAsync(siteUrl));

        // 7. The dashboard lists the client with its plan.
        Assert.Contains("Hassan Minimarket", await c.GetStringAsync("/"));
    }

    [Fact]
    public async Task A_new_link_for_an_existing_installation_replaces_the_old_one_and_can_be_cancelled()
    {
        var c = await SignedInAsync();
        var seeded = await _cloud.SeedSiteAsync(clientName: "Relink");
        var siteUrl = $"/Sites/Details/{seeded.SiteId}";

        await HtmlForms.PostAsync(c, siteUrl, $"{siteUrl}?handler=NewLink");
        var first = Regex.Match(await c.GetStringAsync(siteUrl), @"/enroll/([A-Z0-9-]{14})").Groups[1].Value;
        await HtmlForms.PostAsync(c, siteUrl, $"{siteUrl}?handler=NewLink");
        var second = Regex.Match(await c.GetStringAsync(siteUrl), @"/enroll/([A-Z0-9-]{14})").Groups[1].Value;
        Assert.NotEqual(first, second);

        var client = _cloud.CreateClient();
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/api/enroll", new EnrollRequest(first, null, null), SyncWire.Json)).StatusCode);

        // Cancelling (Disconnect) kills the link that is still open.
        await HtmlForms.PostAsync(c, siteUrl, $"{siteUrl}?handler=Disconnect");
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/api/enroll", new EnrollRequest(second, null, null), SyncWire.Json)).StatusCode);
    }

    [Fact]
    public async Task The_advanced_raw_token_path_still_works_and_shows_the_token_once()
    {
        var c = await SignedInAsync();
        var seeded = await _cloud.SeedSiteAsync(clientName: "Manual");
        var siteUrl = $"/Sites/Details/{seeded.SiteId}";

        await HtmlForms.PostAsync(c, siteUrl, $"{siteUrl}?handler=IssueToken");
        var page = await c.GetStringAsync(siteUrl);
        var token = Regex.Match(page, @"nxp_[A-Za-z0-9_\-]{43}").Value;
        Assert.NotEmpty(token);
        Assert.Contains("online.json", page);
        Assert.DoesNotContain(token, await c.GetStringAsync(siteUrl));
        Assert.Equal(HttpStatusCode.OK, (await _cloud.DeviceClient(token).GetAsync("/api/sync/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cloud.DeviceClient(seeded.Token).GetAsync("/api/sync/ping")).StatusCode);
    }

    [Fact]
    public async Task Extending_suspending_and_deactivating_a_client_changes_what_their_till_may_do()
    {
        var c = await SignedInAsync();
        var seeded = await _cloud.SeedSiteAsync(subscriptionMonths: 1, clientName: "Lifecycle");
        var clientUrl = $"/Clients/Details/{seeded.ClientId}";
        var before = (await _cloud.WithControlDbAsync(db => db.Subscriptions.AsNoTracking().FirstAsync(s => s.ClientId == seeded.ClientId))).ExpiresAt;

        await HtmlForms.PostAsync(c, clientUrl, $"{clientUrl}?handler=Extend&months=12");
        var after = (await _cloud.WithControlDbAsync(db => db.Subscriptions.AsNoTracking().FirstAsync(s => s.ClientId == seeded.ClientId))).ExpiresAt;
        Assert.Equal(before.AddMonths(12), after);

        await HtmlForms.PostAsync(c, clientUrl, $"{clientUrl}?handler=Suspend");
        var ping = await _cloud.DeviceClient(seeded.Token).PingStatusAsync();
        Assert.Equal("Suspended", ping);

        await HtmlForms.PostAsync(c, clientUrl, $"{clientUrl}?handler=Resume");
        Assert.Equal("Active", await _cloud.DeviceClient(seeded.Token).PingStatusAsync());

        await HtmlForms.PostAsync(c, clientUrl, $"{clientUrl}?handler=ToggleActive");
        Assert.Equal(HttpStatusCode.Forbidden, (await _cloud.DeviceClient(seeded.Token).GetAsync("/api/sync/ping")).StatusCode);

        var history = await c.GetStringAsync(clientUrl);
        Assert.Contains("extended by 12 month", history);
        Assert.Contains("suspended", history, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class PingExtensions
{
    public static async Task<string> PingStatusAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/sync/ping");
        var body = await response.Content.ReadAsStringAsync();
        return Regex.Match(body, "\"subscriptionStatus\":\"(\\w+)\"").Groups[1].Value;
    }
}
