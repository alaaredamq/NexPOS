using System.Net;
using Cloud.Data;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cloud.Tests;

public class PortalExperienceTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public PortalExperienceTests(CloudFactory cloud) => _cloud = cloud;

    private async Task<HttpClient> SignedInAsync(CloudFactory? factory = null)
    {
        var c = (factory ?? _cloud).CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var login = await HtmlForms.PostAsync(c, "/Account/Login", "/Account/Login",
            ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return c;
    }

    // ---- contact is recorded whenever a till is heard from ----

    [Fact]
    public async Task Every_contact_updates_last_seen_and_counts_in_the_current_hour()
    {
        using var factory = new CloudFactory(); // own clock: this test moves time
        var site = await factory.SeedSiteAsync();
        var client = factory.DeviceClient(site.Token);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

        await client.GetAsync("/api/sync/ping");
        var seen = await factory.WithControlDbAsync(db => db.Sites.AsNoTracking().Where(s => s.Id == site.SiteId).Select(s => s.LastSeenAt).FirstAsync());
        var firstBeats = await factory.WithControlDbAsync(db => db.SiteHeartbeats.AsNoTracking().Where(h => h.SiteId == site.SiteId).ToListAsync());
        Assert.Equal(now, seen);
        var beat = Assert.Single(firstBeats);
        Assert.Equal(hour, beat.Hour);
        Assert.Equal(1, beat.Contacts);

        // Two minutes later (past the one-a-minute write limit), same hour: the same row counts up.
        factory.Clock.Now = factory.Clock.Now.AddMinutes(2);
        await client.GetAsync("/api/sync/ping");
        var second = await factory.WithControlDbAsync(db => db.SiteHeartbeats.AsNoTracking().Where(h => h.SiteId == site.SiteId).ToListAsync());
        Assert.Equal(2, Assert.Single(second).Contacts);

        // The next hour starts a new row.
        factory.Clock.Now = factory.Clock.Now.AddHours(1);
        await client.GetAsync("/api/sync/ping");
        var third = await factory.WithControlDbAsync(db => db.SiteHeartbeats.AsNoTracking().Where(h => h.SiteId == site.SiteId).ToListAsync());
        Assert.Equal(2, third.Count);
    }

    [Fact]
    public async Task Contact_history_older_than_a_week_is_forgotten()
    {
        var site = await _cloud.SeedSiteAsync();
        var now = _cloud.Clock.GetUtcNow().UtcDateTime;
        var thisHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        await _cloud.WithControlDbAsync(async db =>
        {
            db.SiteHeartbeats.Add(new SiteHeartbeat { SiteId = site.SiteId, Hour = thisHour.AddDays(-9), Contacts = 5 });
            db.SiteHeartbeats.Add(new SiteHeartbeat { SiteId = site.SiteId, Hour = thisHour.AddDays(-3), Contacts = 4 });
            return await db.SaveChangesAsync();
        });

        await _cloud.DeviceClient(site.Token).GetAsync("/api/sync/ping");

        var rows = await _cloud.WithControlDbAsync(db => db.SiteHeartbeats.AsNoTracking().Where(h => h.SiteId == site.SiteId).ToListAsync());
        Assert.DoesNotContain(rows, r => r.Hour < thisHour.AddDays(-7));
        Assert.Contains(rows, r => r.Contacts == 4); // the 3-day-old row stays
    }

    // ---- what the pages show ----

    [Fact]
    public async Task The_site_page_draws_the_24_hour_strip_and_reads_a_quiet_shop_correctly()
    {
        var c = await SignedInAsync();
        var site = await _cloud.SeedSiteAsync(clientName: "Strip Shop");
        await _cloud.DeviceClient(site.Token).GetAsync("/api/sync/ping");

        var page = await c.GetStringAsync($"/Sites/Details/{site.SiteId}");

        Assert.Contains("class=\"pulse lg\"", page);
        Assert.Contains("Contact in each of the last 24 hours: 1 of 24 hours", page);
        Assert.Contains("In contact", page);
        Assert.Contains("Last contact just now", page);
    }

    [Fact]
    public async Task The_clients_list_puts_what_needs_you_first_and_offers_search_and_filters()
    {
        using var factory = new CloudFactory();
        var c = await SignedInAsync(factory);
        var lapsed = await factory.SeedSiteAsync(subscriptionMonths: 1, clientName: "Lapsed Grocer");
        await factory.WithControlDbAsync(async db =>
        {
            (await db.Subscriptions.FirstAsync(s => s.ClientId == lapsed.ClientId)).ExpiresAt = factory.Clock.GetUtcNow().UtcDateTime.AddDays(-40);
            return await db.SaveChangesAsync();
        });

        var page = await c.GetStringAsync("/");

        Assert.Contains("Needs you", page);
        Assert.Contains("Lapsed Grocer", page);
        Assert.Contains("Online ended on", page);
        Assert.Contains("data-chip=\"attention\"", page);
        Assert.Contains("data-search", page);
        Assert.Contains("class=\"pulse \"", page); // every row carries its strip
    }

    [Fact]
    public async Task An_empty_portal_invites_the_first_client()
    {
        using var empty = new CloudFactory();
        var c = await SignedInAsync(empty);

        var page = await c.GetStringAsync("/");

        Assert.Contains("No clients yet", page);
        Assert.Contains("Add the first client", page);
    }

    [Fact]
    public async Task An_action_confirms_itself_once_with_a_toast_in_the_same_words()
    {
        var c = await SignedInAsync();
        var seeded = await _cloud.SeedSiteAsync(clientName: "Toast Shop");
        var url = $"/Clients/Details/{seeded.ClientId}";

        var response = await HtmlForms.PostAsync(c, url, $"{url}?handler=Extend&months=3");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var first = await c.GetStringAsync(url);
        Assert.Contains("data-flash", first);
        Assert.Matches(@"Added 3 months\. Online now ends \d{4}-\d{2}-\d{2}", first);
        Assert.DoesNotContain("data-flash", await c.GetStringAsync(url)); // shown once, not on every visit
    }

    [Fact]
    public async Task Destructive_buttons_ask_first_and_say_what_will_happen()
    {
        var c = await SignedInAsync();
        var seeded = await _cloud.SeedSiteAsync(clientName: "Careful Shop");

        var site = await c.GetStringAsync($"/Sites/Details/{seeded.SiteId}");
        Assert.Contains("data-confirm-title=\"Disconnect this till?\"", site);
        Assert.Contains("The shop keeps selling", site);
        Assert.Contains("data-confirm-title=\"Disable this till?\"", site);

        var client = await c.GetStringAsync($"/Clients/Details/{seeded.ClientId}");
        Assert.Contains("data-confirm-title=\"Suspend Online?\"", client);
        Assert.Contains("data-confirm-title=\"Deactivate this client?\"", client);
    }

    [Fact]
    public async Task The_signed_in_pages_share_one_shell_and_the_public_pages_do_not()
    {
        var c = await SignedInAsync();
        var home = await c.GetStringAsync("/");
        Assert.Contains("class=\"rail\"", home);
        Assert.Contains("aria-label=\"Sign out\"", home);

        var login = await _cloud.CreateClient().GetStringAsync("/Account/Login");
        var enroll = await _cloud.CreateClient().GetStringAsync("/enroll/ABCD-EFGH-JKMN");
        Assert.DoesNotContain("class=\"rail\"", login);
        Assert.DoesNotContain("class=\"rail\"", enroll);
        Assert.Contains("Sign in", login);
    }

    [Fact]
    public async Task The_new_client_form_offers_the_plan_lengths_and_defaults_to_one_month()
    {
        var c = await SignedInAsync();
        var page = await c.GetStringAsync("/Clients/Create");

        foreach (var months in new[] { 0, 1, 3, 6, 12 })
            Assert.Contains($"name=\"Months\" value=\"{months}\"", page);
        Assert.Matches(@"value=""1""\s+checked", page);
    }
}
