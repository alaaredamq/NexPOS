using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>The admin app's API (/api/admin/v1): sign-in, then every portal action as JSON.</summary>
public class AdminApiTests
{
    private const string Api = "/api/admin/v1";

    private static async Task<HttpClient> SignedInAsync(CloudFactory cloud, string device = "Test phone")
    {
        var c = cloud.CreateClient(new() { AllowAutoRedirect = false });
        var login = await c.PostAsJsonAsync($"{Api}/auth/login", new { username = CloudFactory.AdminUser, password = CloudFactory.AdminPassword, deviceName = device });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        Assert.StartsWith("nxa_", token);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task Sign_in_gives_a_token_and_nothing_else_opens_the_api()
    {
        using var cloud = new CloudFactory();
        var anonymous = cloud.CreateClient(new() { AllowAutoRedirect = false });

        // No token: a JSON 401, not the portal's redirect to its sign-in page.
        var none = await anonymous.GetAsync($"{Api}/clients");
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal("unauthorized", (await none.Content.ReadFromJsonAsync<SyncError>(SyncWire.Json))!.Code);

        var wrong = await anonymous.PostAsJsonAsync($"{Api}/auth/login", new { username = CloudFactory.AdminUser, password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("invalid_credentials", (await wrong.Content.ReadFromJsonAsync<SyncError>(SyncWire.Json))!.Code);

        // An owner's phone token is not an admin token.
        var forged = cloud.CreateClient();
        forged.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "nxa_not-a-real-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync($"{Api}/me")).StatusCode);

        // The portal's cookie is never enough: a web page cannot make a signed-in browser call the API.
        var browser = cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await HtmlForms.PostAsync(browser, "/admin/Account/Login", "/admin/Account/Login",
            ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync($"{Api}/clients")).StatusCode);

        var app = await SignedInAsync(cloud, "Alaa's phone");
        var me = await JsonAsync(await app.GetAsync($"{Api}/me"));
        Assert.Equal(CloudFactory.AdminUser, me.GetProperty("username").GetString());
        Assert.Equal("Alaa's phone", me.GetProperty("deviceName").GetString());
        Assert.Equal("$", me.GetProperty("currency").GetString());

        // Only a hash of the token is stored.
        var token = app.DefaultRequestHeaders.Authorization!.Parameter!;
        var stored = await cloud.WithControlDbAsync(db => db.AdminSessions.AsNoTracking().Select(s => s.TokenHash).ToListAsync());
        Assert.DoesNotContain(token, stored);

        // A second device can be seen and signed out from the first.
        var other = await SignedInAsync(cloud, "Old laptop");
        var sessions = await JsonAsync(await app.GetAsync($"{Api}/sessions"));
        Assert.Equal(2, sessions.GetArrayLength());
        var laptop = sessions.EnumerateArray().Single(s => s.GetProperty("deviceName").GetString() == "Old laptop");
        Assert.False(laptop.GetProperty("current").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await app.DeleteAsync($"{Api}/sessions/{laptop.GetProperty("id").GetInt32()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync($"{Api}/me")).StatusCode);

        // Signing out ends the token.
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsync($"{Api}/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync($"{Api}/me")).StatusCode);
    }

    [Fact]
    public async Task Tokens_expire_after_their_lifetime()
    {
        using var cloud = new CloudFactory();
        var app = await SignedInAsync(cloud);
        cloud.Clock.Now = cloud.Clock.Now.AddDays(31);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync($"{Api}/me")).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_api_sign_in_too()
    {
        using var cloud = new CloudFactory();
        var c = cloud.CreateClient();
        for (var i = 0; i < 5; i++)
            await c.PostAsJsonAsync($"{Api}/auth/login", new { username = CloudFactory.AdminUser, password = $"guess-{i}" });
        var locked = await c.PostAsJsonAsync($"{Api}/auth/login", new { username = CloudFactory.AdminUser, password = CloudFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task A_client_is_run_end_to_end_from_the_app()
    {
        using var cloud = new CloudFactory();
        var app = await SignedInAsync(cloud);

        // Refused requests say why, with a code to branch on.
        var noName = await app.PostAsJsonAsync($"{Api}/clients", new { name = " ", months = 1 });
        Assert.Equal("name_required", (await JsonAsync(noName, HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        // Create: the client, its main branch and the link to send, in one call.
        var created = await JsonAsync(await app.PostAsJsonAsync($"{Api}/clients",
            new { name = "Api Market", phone = "03 111 222", email = "shop@example.com", months = 3 }), HttpStatusCode.Created);
        var id = created.GetProperty("client").GetProperty("id").GetInt32();
        var siteId = created.GetProperty("siteId").GetGuid();
        var link = created.GetProperty("link").GetProperty("url").GetString()!;
        Assert.Matches(@"/enroll/[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$", link);
        Assert.Equal("Active", created.GetProperty("client").GetProperty("subscriptionState").GetString());

        // The list finds it by phone, and the overview counts it.
        var found = await JsonAsync(await app.GetAsync($"{Api}/clients?search=111"));
        Assert.Equal("Api Market", found[0].GetProperty("name").GetString());
        Assert.Equal(24, found[0].GetProperty("pulse").GetArrayLength());
        Assert.Equal(1, (await JsonAsync(await app.GetAsync($"{Api}/overview"))).GetProperty("clients").GetInt32());

        // Edit, extend, suspend, resume.
        await JsonAsync(await app.PutAsJsonAsync($"{Api}/clients/{id}", new { name = "Api Market 2", phone = "03 111 222" }));
        var extended = await JsonAsync(await app.PostAsJsonAsync($"{Api}/clients/{id}/subscription/extend", new { months = 12 }));
        Assert.Equal(new DateTime(2028, 2, 5), extended.GetProperty("expiresAtUtc").GetDateTime().Date);
        Assert.Equal("bad_months", (await JsonAsync(await app.PostAsJsonAsync($"{Api}/clients/{id}/subscription/extend", new { months = 0 }), HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        Assert.True((await JsonAsync(await app.PostAsync($"{Api}/clients/{id}/subscription/suspend", null))).GetProperty("suspended").GetBoolean());
        Assert.False((await JsonAsync(await app.PostAsync($"{Api}/clients/{id}/subscription/resume", null))).GetProperty("suspended").GetBoolean());

        // The shop connects with the link; the branch then shows connected, and can be disconnected.
        var enrolled = await cloud.CreateClient().PostAsJsonAsync("/api/enroll",
            new EnrollRequest(link[(link.LastIndexOf('/') + 1)..], "ABCDE-12345-ABCDE-12345", "SHOP-PC"), SyncWire.Json);
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var site = await JsonAsync(await app.GetAsync($"{Api}/sites/{siteId}"));
        Assert.Equal("Connected", site.GetProperty("site").GetProperty("connection").GetString());
        Assert.Equal("Disconnected", (await JsonAsync(await app.PostAsync($"{Api}/sites/{siteId}/disconnect", null))).GetProperty("site").GetProperty("connection").GetString());

        // A new link, a manual token, a resync, disable and enable.
        var newLink = await JsonAsync(await app.PostAsync($"{Api}/sites/{siteId}/link", null), HttpStatusCode.Created);
        Assert.StartsWith("https://wa.me/?text=", newLink.GetProperty("whatsAppUrl").GetString());
        var token = (await JsonAsync(await app.PostAsync($"{Api}/sites/{siteId}/token", null), HttpStatusCode.Created)).GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await cloud.DeviceClient(token).GetAsync("/api/sync/ping")).StatusCode);
        Assert.True((await JsonAsync(await app.PostAsync($"{Api}/sites/{siteId}/resync", null))).GetProperty("site").GetProperty("resyncRequested").GetBoolean());
        Assert.True((await JsonAsync(await app.PostAsync($"{Api}/sites/{siteId}/disable", null))).GetProperty("site").GetProperty("isDisabled").GetBoolean());
        Assert.False((await JsonAsync(await app.PostAsync($"{Api}/sites/{siteId}/enable", null))).GetProperty("site").GetProperty("isDisabled").GetBoolean());

        // A second branch, renamed, then removed.
        var second = await JsonAsync(await app.PostAsJsonAsync($"{Api}/clients/{id}/sites", new { name = "Second shop" }), HttpStatusCode.Created);
        var secondId = second.GetProperty("site").GetProperty("id").GetGuid();
        Assert.Equal("Uptown", (await JsonAsync(await app.PutAsJsonAsync($"{Api}/sites/{secondId}", new { name = "Uptown" }))).GetProperty("site").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await app.DeleteAsync($"{Api}/sites/{secondId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.GetAsync($"{Api}/sites/{secondId}")).StatusCode);

        // History records what was done.
        var events = await JsonAsync(await app.GetAsync($"{Api}/clients/{id}/events"));
        var texts = events.EnumerateArray().Select(e => e.GetProperty("text").GetString()).ToList();
        Assert.Contains(texts, t => t!.StartsWith("Online extended by 12 month(s)"));
        Assert.Contains(texts, t => t == "Branch 'Uptown' removed.");

        // Deactivate, reactivate, remove.
        Assert.False((await JsonAsync(await app.PostAsync($"{Api}/clients/{id}/deactivate", null))).GetProperty("isActive").GetBoolean());
        Assert.True((await JsonAsync(await app.PostAsync($"{Api}/clients/{id}/activate", null))).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await app.DeleteAsync($"{Api}/clients/{id}")).StatusCode);
        Assert.Equal("not_found", (await JsonAsync(await app.GetAsync($"{Api}/clients/{id}"), HttpStatusCode.NotFound)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Phone_logins_are_handed_over_once_and_managed_from_the_app()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Login Shop");
        var app = await SignedInAsync(cloud);
        var url = $"{Api}/clients/{seeded.ClientId}/logins";

        var added = await JsonAsync(await app.PostAsJsonAsync(url, new { signIn = "owner@loginshop.com", role = "manager" }), HttpStatusCode.Created);
        var password = added.GetProperty("password").GetString()!;
        var loginId = added.GetProperty("login").GetProperty("id").GetInt32();
        Assert.Equal("manager", added.GetProperty("login").GetProperty("role").GetString());
        Assert.Equal(12, password.Length);

        // The made-up password really signs the owner in.
        var phone = cloud.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await phone.PostAsJsonAsync("/owner/v1/auth/login", new { email = "owner@loginshop.com", password })).StatusCode);

        Assert.Equal("sign_in_taken", (await JsonAsync(await app.PostAsJsonAsync(url, new { signIn = "owner@loginshop.com" }), HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        Assert.Equal("staff", (await JsonAsync(await app.PutAsJsonAsync($"{url}/{loginId}", new { role = "staff" }))).GetProperty("role").GetString());
        Assert.False((await JsonAsync(await app.PutAsJsonAsync($"{url}/{loginId}", new { isActive = false }))).GetProperty("isActive").GetBoolean());

        var reset = await JsonAsync(await app.PostAsync($"{url}/{loginId}/reset-password", null));
        Assert.NotEqual(password, reset.GetProperty("password").GetString());

        Assert.Equal(1, (await JsonAsync(await app.GetAsync(url))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await app.DeleteAsync($"{url}/{loginId}")).StatusCode);
        Assert.Equal(0, (await JsonAsync(await app.GetAsync(url))).GetArrayLength());

        // Another client's id in the address cannot reach this client's login.
        var other = await cloud.SeedSiteAsync(clientName: "Other Shop");
        var mine = await JsonAsync(await app.PostAsJsonAsync(url, new { signIn = "second@loginshop.com" }), HttpStatusCode.Created);
        var mineId = mine.GetProperty("login").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await app.DeleteAsync($"{Api}/clients/{other.ClientId}/logins/{mineId}")).StatusCode);
    }

    [Fact]
    public async Task The_books_are_kept_from_the_app()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Paying Shop");
        var app = await SignedInAsync(cloud);
        var f = $"{Api}/finance";

        var options = await JsonAsync(await app.GetAsync($"{f}/options"));
        Assert.Contains("Whish Money", options.GetProperty("methods").EnumerateArray().Select(x => x.GetString()));

        var bad = await app.PostAsJsonAsync($"{f}/payments", new { clientId = seeded.ClientId, amount = -5, date = "2026-11-02" });
        Assert.Equal("invalid_payment", (await JsonAsync(bad, HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        var paid = await JsonAsync(await app.PostAsJsonAsync($"{f}/payments",
            new { clientId = seeded.ClientId, amount = 150.5m, date = "2026-11-02", method = "Whish", purpose = "Online subscription" }), HttpStatusCode.Created);
        var paymentId = paid.GetProperty("id").GetInt32();
        Assert.Equal("Paying Shop", paid.GetProperty("payer").GetString());
        Assert.Equal(CloudFactory.AdminUser, paid.GetProperty("createdBy").GetString());
        await JsonAsync(await app.PostAsJsonAsync($"{f}/payments", new { payer = "Owner capital", amount = 1000, date = "2026-10-01" }), HttpStatusCode.Created);

        var spent = await JsonAsync(await app.PostAsJsonAsync($"{f}/expenses",
            new { amount = 40, date = "2026-11-03", category = "Hosting & servers", payee = "MonsterASP" }), HttpStatusCode.Created);
        var expenseId = spent.GetProperty("id").GetInt32();

        // Correct a typo instead of deleting and re-adding.
        var fixedPay = await JsonAsync(await app.PutAsJsonAsync($"{f}/payments/{paymentId}",
            new { clientId = seeded.ClientId, amount = 160, date = "2026-11-02", method = "Whish", purpose = "Online subscription" }));
        Assert.Equal(160, fixedPay.GetProperty("amount").GetDecimal());
        await JsonAsync(await app.PutAsJsonAsync($"{f}/expenses/{expenseId}", new { amount = 45, date = "2026-11-03", category = "Hosting & servers" }));

        Assert.Equal(1, (await JsonAsync(await app.GetAsync($"{f}/payments?from=2026-11-01&to=2026-11-30"))).GetArrayLength());
        Assert.Equal(1, (await JsonAsync(await app.GetAsync($"{Api}/clients/{seeded.ClientId}/payments"))).GetArrayLength());
        Assert.Equal(1, (await JsonAsync(await app.GetAsync($"{f}/expenses?category=hosting%20%26%20servers"))).GetArrayLength());

        // A budget set in October carries into November, until one is set there.
        var budget = await JsonAsync(await app.PutAsJsonAsync($"{f}/budgets/2026-10", new { incomeTarget = 500, spendingLimit = 30 }));
        Assert.Equal("2026-10", budget.GetProperty("month").GetString());
        var november = await JsonAsync(await app.GetAsync($"{f}/budgets/2026-11"));
        Assert.Equal("2026-10", november.GetProperty("setIn").GetString());
        Assert.Equal("bad_month", (await JsonAsync(await app.PutAsJsonAsync($"{f}/budgets/nonsense", new { incomeTarget = 1 }), HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        var summary = await JsonAsync(await app.GetAsync($"{f}/summary?m=2026-11"));
        Assert.Equal(160, summary.GetProperty("collected").GetDecimal());
        Assert.Equal(45, summary.GetProperty("spent").GetDecimal());
        Assert.Equal(115, summary.GetProperty("profit").GetDecimal());
        Assert.Equal(1115, summary.GetProperty("balance").GetDecimal());
        Assert.True(summary.GetProperty("budget").GetProperty("spending").GetProperty("over").GetBoolean());
        Assert.Equal(12, summary.GetProperty("trend").GetArrayLength());
        Assert.Equal(2, summary.GetProperty("lines").GetArrayLength());
        Assert.Contains(summary.GetProperty("attention").EnumerateArray(), a => a.GetProperty("title").GetString() == "Over the spending limit");
        Assert.Equal("2026-10", summary.GetProperty("period").GetProperty("previous").GetString()![2..]);

        var year = await JsonAsync(await app.GetAsync($"{f}/summary?y=2026"));
        Assert.Equal(1160, year.GetProperty("collected").GetDecimal());

        var csv = await app.GetAsync($"{f}/export?m=2026-11");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("2026-11-02,In,160.00,Paying Shop,Whish Money", await csv.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await app.DeleteAsync($"{f}/budgets/2026-10")).StatusCode);
        Assert.Null((await JsonAsync(await app.GetAsync($"{f}/budgets/2026-11"))).GetProperty("setIn").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await app.DeleteAsync($"{f}/payments/{paymentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.GetAsync($"{f}/payments/{paymentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.DeleteAsync($"{f}/expenses/{expenseId}")).StatusCode);
    }

    [Fact]
    public async Task The_app_can_edit_a_clients_online_menu()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Menu Shop");
        var app = await SignedInAsync(cloud);
        var editor = await JsonAsync(await app.GetAsync($"/menu/v1/editor?client={seeded.ClientId}"));
        Assert.Equal("Menu Shop", editor.GetProperty("clientName").GetString());
        Assert.True(editor.GetProperty("admin").GetBoolean());

        var anonymous = cloud.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "nxa_bogus");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/menu/v1/editor?client={seeded.ClientId}")).StatusCode);
    }
}
