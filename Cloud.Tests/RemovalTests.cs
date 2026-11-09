using System.Net;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>Removing a branch or a client takes its cloud copy with it and nothing else.</summary>
public class RemovalTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public RemovalTests(CloudFactory cloud) => _cloud = cloud;

    private async Task<HttpClient> SignedInAsync()
    {
        var c = _cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var response = await HtmlForms.PostAsync(c, "/Account/Login", "/Account/Login",
            ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return c;
    }

    /// <summary>A second branch for the seeded client, with everything a connected branch has, plus a mirror file on disk.</summary>
    private async Task<Guid> AddBusyBranchAsync(int clientId, string name)
    {
        var id = Guid.NewGuid();
        var now = _cloud.Clock.GetUtcNow().UtcDateTime;
        await _cloud.WithControlDbAsync(async db =>
        {
            var (_, hash, prefix) = DeviceTokens.Generate();
            var site = new Site { Id = id, ClientId = clientId, Name = name, CreatedAt = now };
            site.Tokens.Add(new DeviceToken { TokenHash = hash, Prefix = prefix, CreatedAt = now });
            site.EnrollmentCodes.Add(new EnrollmentCode { CodeHash = Guid.NewGuid().ToString("N"), CreatedAt = now, ExpiresAt = now.AddDays(7) });
            site.Heartbeats.Add(new SiteHeartbeat { Hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc), Contacts = 3 });
            db.Sites.Add(site);
            db.SyncBatchLogs.Add(new SyncBatchLog { SiteId = id, ReceivedAt = now, Outcome = "applied" });
            await db.SaveChangesAsync();
            return 0;
        });
        _cloud.Mirrors.EnsureReady(id);
        Assert.True(File.Exists(_cloud.Mirrors.PathFor(id)));
        return id;
    }

    private async Task<(int Sites, int Tokens, int Codes, int Beats, int Logs)> RowsOfAsync(Guid siteId)
        => await _cloud.WithControlDbAsync(async db => (
            await db.Sites.CountAsync(s => s.Id == siteId),
            await db.DeviceTokens.CountAsync(t => t.SiteId == siteId),
            await db.EnrollmentCodes.CountAsync(c => c.SiteId == siteId),
            await db.SiteHeartbeats.CountAsync(h => h.SiteId == siteId),
            await db.SyncBatchLogs.CountAsync(l => l.SiteId == siteId)));

    private bool MirrorFilesExist(Guid siteId)
        => new[] { "", "-wal", "-shm" }.Any(suffix => File.Exists(_cloud.Mirrors.PathFor(siteId) + suffix));

    [Fact]
    public async Task Removing_a_branch_deletes_its_cloud_copy_and_credentials_and_leaves_a_history_line()
    {
        var c = await SignedInAsync();
        var seeded = await _cloud.SeedSiteAsync(clientName: "Two Branch Shop");
        var doomed = await AddBusyBranchAsync(seeded.ClientId, "Testing");
        var kept = await AddBusyBranchAsync(seeded.ClientId, "Kept");

        var response = await HtmlForms.PostAsync(c, $"/Sites/Details/{doomed}", $"/Sites/Details/{doomed}?handler=Remove");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Clients/Details/{seeded.ClientId}", response.Headers.Location!.OriginalString);
        Assert.Equal((0, 0, 0, 0, 0), await RowsOfAsync(doomed));
        Assert.False(MirrorFilesExist(doomed));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/Sites/Details/{doomed}")).StatusCode);

        // Its neighbours are untouched.
        Assert.Equal((1, 1, 1, 1, 1), await RowsOfAsync(kept));
        Assert.True(MirrorFilesExist(kept));
        Assert.Equal(1, await _cloud.WithControlDbAsync(db => db.Sites.CountAsync(s => s.Id == seeded.SiteId)));

        var history = await _cloud.WithControlDbAsync(db => db.ClientEvents.AsNoTracking().Where(e => e.ClientId == seeded.ClientId).ToListAsync());
        Assert.Contains(history, e => e.Text == "Branch 'Testing' removed.");
    }

    [Fact]
    public async Task A_removed_branchs_device_token_stops_working()
    {
        var c = await SignedInAsync();
        var seeded = await _cloud.SeedSiteAsync();
        Assert.Equal(HttpStatusCode.OK, (await _cloud.DeviceClient(seeded.Token).GetAsync("/api/sync/ping")).StatusCode);

        await HtmlForms.PostAsync(c, $"/Sites/Details/{seeded.SiteId}", $"/Sites/Details/{seeded.SiteId}?handler=Remove");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _cloud.DeviceClient(seeded.Token).GetAsync("/api/sync/ping")).StatusCode);
    }

    [Fact]
    public async Task Removing_a_client_removes_all_its_branches_its_subscription_and_its_history()
    {
        var c = await SignedInAsync();
        var seeded = await _cloud.SeedSiteAsync(clientName: "Closing Down");
        var second = await AddBusyBranchAsync(seeded.ClientId, "Second");
        var other = await _cloud.SeedSiteAsync(clientName: "Staying Open");

        var response = await HtmlForms.PostAsync(c, $"/Clients/Details/{seeded.ClientId}", $"/Clients/Details/{seeded.ClientId}?handler=Remove");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/Clients/Details/{seeded.ClientId}")).StatusCode);
        Assert.Equal((0, 0, 0, 0, 0), await RowsOfAsync(second));
        Assert.Equal(0, await _cloud.WithControlDbAsync(db => db.Sites.CountAsync(s => s.ClientId == seeded.ClientId)));
        Assert.Equal(0, await _cloud.WithControlDbAsync(db => db.Subscriptions.CountAsync(s => s.ClientId == seeded.ClientId)));
        Assert.Equal(0, await _cloud.WithControlDbAsync(db => db.ClientEvents.CountAsync(e => e.ClientId == seeded.ClientId)));
        Assert.False(MirrorFilesExist(second));

        // Another client is untouched, and the home page no longer lists the removed one.
        Assert.Equal(1, await _cloud.WithControlDbAsync(db => db.Sites.CountAsync(s => s.Id == other.SiteId)));
        var home = await c.GetStringAsync("/");
        Assert.DoesNotContain($"/Clients/Details/{seeded.ClientId}\"", home);
        Assert.Contains($"/Clients/Details/{other.ClientId}\"", home);
    }

    [Fact]
    public async Task Removing_needs_a_signed_in_admin()
    {
        var seeded = await _cloud.SeedSiteAsync();
        var anonymous = _cloud.CreateClient(new() { AllowAutoRedirect = false });

        var branch = await anonymous.PostAsync($"/Sites/Details/{seeded.SiteId}?handler=Remove", new FormUrlEncodedContent([]));
        var client = await anonymous.PostAsync($"/Clients/Details/{seeded.ClientId}?handler=Remove", new FormUrlEncodedContent([]));

        Assert.NotEqual(HttpStatusCode.OK, branch.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, client.StatusCode);
        Assert.Equal(1, await _cloud.WithControlDbAsync(db => db.Sites.CountAsync(s => s.Id == seeded.SiteId)));
        Assert.Equal(1, await _cloud.WithControlDbAsync(db => db.Clients.CountAsync(x => x.Id == seeded.ClientId)));
    }

    [Fact]
    public async Task The_home_page_shows_each_clients_product_and_order_counts_even_when_they_are_sealed()
    {
        var c = await SignedInAsync();
        var shop = await _cloud.SeedSiteAsync(clientName: "Counted Shop");
        var quiet = await _cloud.SeedSiteAsync(clientName: "Quiet Shop");

        using var till = new TillDb();
        await till.AddProductAsync("Olive oil 1L", "6001");
        await till.AddProductAsync("Bread", "6002");
        till.EnableSync();
        Assert.True((await till.Pusher(Transports.To(_cloud, shop.Token), sealer: new RowSealer(RowSealer.NewKey())).RunOnceAsync(default)).Ok);
        var products = SqlHelper.Scalar<long>(till.Path, "SELECT COUNT(*) FROM Products");
        var orders = SqlHelper.Scalar<long>(till.Path, "SELECT COUNT(*) FROM Orders");

        var home = await c.GetStringAsync("/");

        Assert.Contains($"<b>{products:N0}</b> products", home);
        Assert.Contains($"<b>{orders:N0}</b> orders", home);
        Assert.Contains("No data yet", home); // the shop that never uploaded
        Assert.Contains("1 branch", home);
        Assert.DoesNotContain("1 till", home);
        _ = quiet;
    }
}
