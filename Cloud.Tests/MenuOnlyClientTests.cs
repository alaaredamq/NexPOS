using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// One subscription, Online, covers the cloud copy, the phone app and the online menu. A client can buy only the menu ("Online menu only":
/// months, no POS, no branch, no licence), and every client's public menu goes offline when its Online months (and grace week) run out.
/// </summary>
public class MenuOnlyClientTests
{
    private const string Api = "/api/admin/v1";
    private const string Password = "pass-1234-ok";

    private static async Task<HttpClient> PortalAsync(CloudFactory cloud)
    {
        var c = cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await HtmlForms.PostAsync(c, "/admin/Account/Login", "/admin/Account/Login",
            ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        return c;
    }

    private static async Task<HttpClient> ApiAsync(CloudFactory cloud)
    {
        var c = cloud.CreateClient(new() { AllowAutoRedirect = false });
        var login = await c.PostAsJsonAsync($"{Api}/auth/login", new { username = CloudFactory.AdminUser, password = CloudFactory.AdminPassword });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return c;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>The owner's login for <paramref name="clientId"/>, signed in on the owner website.</summary>
    private static async Task<HttpClient> OwnerAsync(CloudFactory cloud, int clientId, string stem)
    {
        await cloud.WithControlDbAsync(async db =>
        {
            db.OwnerUsers.Add(new OwnerUser
            {
                ClientId = clientId, Email = $"owner@{stem}.test", DisplayName = "Owner", PasswordHash = PasswordHasher.Hash(Password),
                CreatedAt = cloud.Clock.GetUtcNow().UtcDateTime, Permissions = (int)OwnerRoles.For("owner"),
            });
            return await db.SaveChangesAsync();
        });
        return await SignInAsync(cloud, stem);
    }

    /// <summary>A fresh sign-in (a session does not last the year the tests jump over).</summary>
    private static async Task<HttpClient> SignInAsync(CloudFactory cloud, string stem)
    {
        var http = cloud.CreateClient(new() { AllowAutoRedirect = false });
        var login = await JsonAsync(await http.PostAsJsonAsync("/owner/v1/auth/login", new { email = $"owner@{stem}.test", password = Password }));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("token").GetString());
        return http;
    }

    /// <summary>A published menu with one dish, made by the owner; its slug.</summary>
    private static async Task<string> PublishedMenuAsync(HttpClient owner, string title)
    {
        var state = await JsonAsync(await owner.PostAsJsonAsync("/menu/v1/editor", new { title }));
        await JsonAsync(await owner.PostAsJsonAsync("/menu/v1/items", new { name = "Mint lemonade", price = 3m }));
        await JsonAsync(await owner.PutAsJsonAsync("/menu/v1/settings", new { isPublished = true }));
        return state.GetProperty("menu").GetProperty("slug").GetString()!;
    }

    private static int IdFrom(string detailsUrl) =>
        int.Parse(detailsUrl["/admin/Clients/Details/".Length..(detailsUrl.IndexOf('#') is var i and >= 0 ? i : detailsUrl.Length)]);

    [Fact]
    public async Task A_menu_only_client_gets_its_months_and_no_branch_or_licence()
    {
        using var cloud = new CloudFactory();
        var c = await PortalAsync(cloud);

        // The menu is part of Online, so it cannot be sold with no months.
        var refused = await HtmlForms.PostAsync(c, "/admin/Clients/Create", "/admin/Clients/Create",
            ("Name", "Free Lunch Cafe"), ("Type", "menu"), ("Months", "0"));
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Pick the months of Online they paid for", await refused.Content.ReadAsStringAsync());
        Assert.False(await cloud.WithControlDbAsync(db => db.Clients.AnyAsync(x => x.Name == "Free Lunch Cafe")));

        var created = await HtmlForms.PostAsync(c, "/admin/Clients/Create", "/admin/Clients/Create",
            ("Name", "Juice Corner"), ("Type", "menu"), ("Months", "12"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var url = created.Headers.Location!.OriginalString;
        Assert.Matches(@"^/admin/Clients/Details/\d+#phone$", url); // next: their phone login, to edit the menu
        var id = IdFrom(url);

        var client = await cloud.WithControlDbAsync(db => db.Clients.Include(x => x.Sites).Include(x => x.Subscription).AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.True(client.HasOnline);
        Assert.False(client.HasPos);
        Assert.Equal(ClientKinds.Menu, ClientKinds.Of(client));
        Assert.Empty(client.Sites);
        Assert.Equal(cloud.Clock.GetUtcNow().UtcDateTime.AddMonths(12), client.Subscription!.ExpiresAt);

        var page = await c.GetStringAsync($"/admin/Clients/Details/{id}");
        Assert.Contains("Online menu only", page);
        Assert.Contains("They bought the POS", page);
        Assert.DoesNotContain("Issue a key", page); // no POS, no licence
        Assert.DoesNotContain("Add a branch", page);
        Assert.DoesNotContain("Make POS only", page);

        var list = await c.GetStringAsync("/admin");
        Assert.Contains("Online menu only", list);
        Assert.DoesNotContain("No branch added yet", list);

        // The admin API makes one too, and refuses one with no months.
        var app = await ApiAsync(cloud);
        var made = await JsonAsync(await app.PostAsJsonAsync($"{Api}/clients", new { name = "Api Menu", type = "menu", months = 6 }), HttpStatusCode.Created);
        Assert.Equal("menu", made.GetProperty("client").GetProperty("type").GetString());
        Assert.True(made.GetProperty("client").GetProperty("menuLive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, made.GetProperty("link").ValueKind);
        var none = await app.PostAsJsonAsync($"{Api}/clients", new { name = "Api Free", type = "menu", months = 0 });
        Assert.False(none.IsSuccessStatusCode);
        Assert.Contains("months_required", await none.Content.ReadAsStringAsync());
        var rows = await JsonAsync(await app.GetAsync($"{Api}/clients?filter=menu"));
        Assert.Equal(["Api Menu", "Juice Corner"], rows.EnumerateArray().Select(r => r.GetProperty("name").GetString()).Order().ToArray());
    }

    [Fact]
    public async Task The_menu_goes_offline_when_Online_runs_out_and_comes_back_when_renewed()
    {
        using var cloud = new CloudFactory();
        var c = await PortalAsync(cloud);
        var created = await HtmlForms.PostAsync(c, "/admin/Clients/Create", "/admin/Clients/Create",
            ("Name", "Sunset Grill"), ("Type", "menu"), ("Months", "12"));
        var id = IdFrom(created.Headers.Location!.OriginalString);
        var owner = await OwnerAsync(cloud, id, "sunset");

        // A menu-only shop has no till: the owner website opens the menu editor.
        Assert.False((await JsonAsync(await owner.GetAsync("/owner/v1/me"))).GetProperty("hasTill").GetBoolean());
        var slug = await PublishedMenuAsync(owner, "Sunset Grill");
        var guest = cloud.CreateClient();
        Assert.Contains("Mint lemonade", await guest.GetStringAsync($"/m/{slug}"));

        // The 12 months end: during the grace week the menu still answers, and the owner is told when it closes.
        var ends = cloud.Clock.Now.AddMonths(12);
        cloud.Clock.Now = ends.AddDays(3);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/m/{slug}")).StatusCode);
        owner = await SignInAsync(cloud, "sunset"); // an expired plan still lets the owner in, to edit and to see why
        c = await PortalAsync(cloud);
        var online = (await JsonAsync(await owner.GetAsync("/menu/v1/editor"))).GetProperty("online");
        Assert.Equal("Grace", online.GetProperty("state").GetString());
        Assert.True(online.GetProperty("menuLive").GetBoolean());
        Assert.Contains("grace week: their menu goes offline", await c.GetStringAsync("/admin"));

        // After the grace week: offline everywhere a guest could reach it. Nothing is deleted and the owner can still edit.
        cloud.Clock.Now = ends.AddDays(SubscriptionRules.GraceDays + 1);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/m/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/m/{slug}/items?section=0")).StatusCode);
        Assert.DoesNotContain($"/m/{slug}</loc>", await guest.GetStringAsync("/sitemap.xml"));
        var order = await guest.PostAsJsonAsync($"/m/{slug}/orders", new { name = "Guest", lines = new[] { new { id = 1, n = 1 } } });
        Assert.Equal(HttpStatusCode.NotFound, order.StatusCode);

        var editor = await JsonAsync(await owner.GetAsync("/menu/v1/editor"));
        Assert.Equal("Expired", editor.GetProperty("online").GetProperty("state").GetString());
        Assert.False(editor.GetProperty("online").GetProperty("menuLive").GetBoolean());
        await JsonAsync(await owner.PostAsJsonAsync("/menu/v1/items", new { name = "Iced tea", price = 2.5m }));

        c = await PortalAsync(cloud);
        Assert.Contains("their online menu is offline", await c.GetStringAsync("/admin"));
        Assert.Contains(">offline<", await c.GetStringAsync($"/admin/Clients/Details/{id}"));

        // They pay again: 12 more months from today, and the menu is back at once.
        await HtmlForms.PostAsync(c, $"/admin/Clients/Details/{id}", $"/admin/Clients/Details/{id}?handler=Extend&months=12");
        var html = await guest.GetStringAsync($"/m/{slug}");
        Assert.Contains("Mint lemonade", html);
        Assert.Contains("Iced tea", html);
        Assert.Contains($"/m/{slug}</loc>", await guest.GetStringAsync("/sitemap.xml"));
    }

    [Fact]
    public async Task A_POS_and_Online_client_with_no_months_has_its_menu_offline_and_flagged()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(subscriptionMonths: 0, clientName: "Zero Months Diner");
        var owner = await OwnerAsync(cloud, seeded.ClientId, "zero");
        var slug = await PublishedMenuAsync(owner, "Zero Months Diner");

        Assert.Equal(HttpStatusCode.NotFound, (await cloud.CreateClient().GetAsync($"/m/{slug}")).StatusCode);
        var c = await PortalAsync(cloud);
        Assert.Contains("Online menu offline: no months of Online", await c.GetStringAsync("/admin"));

        await HtmlForms.PostAsync(c, $"/admin/Clients/Details/{seeded.ClientId}", $"/admin/Clients/Details/{seeded.ClientId}?handler=Extend&months=12");
        Assert.Equal(HttpStatusCode.OK, (await cloud.CreateClient().GetAsync($"/m/{slug}")).StatusCode);
        Assert.DoesNotContain("Online menu offline", await c.GetStringAsync("/admin"));
    }

    [Fact]
    public async Task A_menu_only_client_that_buys_the_POS_gets_a_branch_and_keeps_its_months()
    {
        using var cloud = new CloudFactory();
        var app = await ApiAsync(cloud);
        var made = await JsonAsync(await app.PostAsJsonAsync($"{Api}/clients", new { name = "Grows Up Cafe", type = "menu", months = 12 }), HttpStatusCode.Created);
        var id = made.GetProperty("client").GetProperty("id").GetInt32();

        // Online cannot be taken away from a client whose only purchase is the menu.
        var off = await app.PostAsync($"{Api}/clients/{id}/online/disable", null);
        Assert.False(off.IsSuccessStatusCode);
        Assert.Contains("menu_only", await off.Content.ReadAsStringAsync());

        var c = await PortalAsync(cloud);
        var added = await HtmlForms.PostAsync(c, $"/admin/Clients/Details/{id}", $"/admin/Clients/Details/{id}?handler=AddPos");
        Assert.Matches(@"^/admin/Sites/Details/[0-9a-f-]{36}$", added.Headers.Location!.OriginalString);
        Assert.Contains("/enroll/", await c.GetStringAsync(added.Headers.Location!.OriginalString));

        var client = await cloud.WithControlDbAsync(db => db.Clients.Include(x => x.Sites).Include(x => x.Subscription).AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Equal(ClientKinds.Online, ClientKinds.Of(client));
        Assert.Single(client.Sites);
        Assert.Equal(cloud.Clock.GetUtcNow().UtcDateTime.AddMonths(12), client.Subscription!.ExpiresAt);
        Assert.Contains("Issue a key", await c.GetStringAsync($"/admin/Clients/Details/{id}"));
    }
}
