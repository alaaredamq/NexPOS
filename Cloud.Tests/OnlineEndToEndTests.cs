using Cloud.Data;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>A real till database pushing to the real cloud app (in memory), then reading the site's mirror file back.</summary>
public class OnlineEndToEndTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public OnlineEndToEndTests(CloudFactory cloud) => _cloud = cloud;

    private string MirrorPath(SeededSite site) => _cloud.Mirrors.PathFor(site.SiteId);

    [Fact]
    public async Task First_push_copies_the_shop_to_the_mirror_without_credentials()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        var customerId = await till.AddCustomerAsync("Layla");
        var productId = await till.AddProductAsync("Olive oil 1L", "6001");
        till.EnableSync();

        var status = new SyncStatus();
        var result = await till.Pusher(Transports.To(_cloud, site.Token), status).RunOnceAsync(default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(0, result.Pending);
        Assert.True(result.ChangesSent > 0);

        var mirror = MirrorPath(site);
        Assert.Equal("Layla", SqlHelper.Scalar<string>(mirror, $"SELECT FirstName FROM Customers WHERE Id = {customerId}"));
        Assert.Equal("Olive oil 1L", SqlHelper.Scalar<string>(mirror, $"SELECT Name FROM Products WHERE Id = {productId}"));

        // Every table's row count matches the till (nothing dropped, nothing duplicated).
        foreach (var table in till.Schema.Tables)
        {
            var local = SqlHelper.Scalar<long>(till.Path, $"SELECT COUNT(*) FROM \"{table.Name}\"");
            var remote = SqlHelper.Scalar<long>(mirror, $"SELECT COUNT(*) FROM \"{table.Name}\"");
            Assert.True(local == remote, $"{table.Name}: till has {local}, mirror has {remote}");
        }

        // The till's real hashes exist locally but never reach the cloud.
        Assert.True(SqlHelper.Scalar<long>(till.Path, "SELECT COUNT(*) FROM Users WHERE PasswordHash <> ''") > 0);
        Assert.Equal(0L, SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM Users WHERE PasswordHash <> '' OR COALESCE(PinHash, '') <> ''"));
        Assert.Equal(
            SqlHelper.Scalar<string>(till.Path, "SELECT GROUP_CONCAT(Username) FROM (SELECT Username FROM Users ORDER BY Id)"),
            SqlHelper.Scalar<string>(mirror, "SELECT GROUP_CONCAT(Username) FROM (SELECT Username FROM Users ORDER BY Id)"));

        var state = await _cloud.WithControlDbAsync(db => db.Sites.AsNoTracking().FirstAsync(s => s.Id == site.SiteId));
        Assert.NotNull(state.LastSyncAt);
        Assert.False(string.IsNullOrEmpty(state.LastSchemaVersion));

        var snapshot = status.Snapshot();
        Assert.NotNull(snapshot.LastSuccessUtc);
        Assert.Null(snapshot.LastError);
    }

    [Fact]
    public async Task Later_changes_follow_updates_deletes_and_new_rows()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        var customerId = await till.AddCustomerAsync("Omar", city: "Beirut");
        var doomedProduct = await till.AddProductAsync("Doomed", "D-1");
        till.EnableSync();
        var pusher = till.Pusher(Transports.To(_cloud, site.Token));
        Assert.True((await pusher.RunOnceAsync(default)).Ok);

        // Edit via EF, via a bulk update (no SaveChanges), delete, and add.
        await using (var context = till.CreateContext())
        {
            var customer = await context.Customers.FirstAsync(c => c.Id == customerId);
            customer.PhoneNumber = "03 123 456";
            await context.SaveChangesAsync();
            await context.Customers.Where(c => c.Id == customerId).ExecuteUpdateAsync(s => s.SetProperty(c => c.City, "Saida"));
            await context.Products.Where(p => p.Id == doomedProduct).ExecuteDeleteAsync();
        }
        var newCustomer = await till.AddCustomerAsync("Nour");

        var result = await pusher.RunOnceAsync(default);
        Assert.True(result.Ok, result.Error);

        var mirror = MirrorPath(site);
        Assert.Equal("Saida", SqlHelper.Scalar<string>(mirror, $"SELECT City FROM Customers WHERE Id = {customerId}"));
        Assert.Equal("03 123 456", SqlHelper.Scalar<string>(mirror, $"SELECT PhoneNumber FROM Customers WHERE Id = {customerId}"));
        Assert.Equal(0L, SqlHelper.Scalar<long>(mirror, $"SELECT COUNT(*) FROM Products WHERE Id = {doomedProduct}"));
        Assert.Equal("Nour", SqlHelper.Scalar<string>(mirror, $"SELECT FirstName FROM Customers WHERE Id = {newCustomer}"));
    }

    [Fact]
    public async Task Many_edits_of_one_row_cost_one_change_carrying_the_latest_values()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        var id = await till.AddCustomerAsync("Rami");
        till.EnableSync();
        var pusher = till.Pusher(Transports.To(_cloud, site.Token));
        Assert.True((await pusher.RunOnceAsync(default)).Ok);

        await using (var context = till.CreateContext())
            for (var i = 1; i <= 25; i++)
                await context.Customers.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.City, $"City {i}"));

        var result = await pusher.RunOnceAsync(default);
        Assert.Equal(1, result.ChangesSent);
        Assert.Equal("City 25", SqlHelper.Scalar<string>(MirrorPath(site), $"SELECT City FROM Customers WHERE Id = {id}"));
    }

    [Fact]
    public async Task First_snapshot_replaces_whatever_the_mirror_held()
    {
        var site = await _cloud.SeedSiteAsync();
        _cloud.Mirrors.EnsureReady(site.SiteId);
        SqlHelper.Exec(MirrorPath(site),
            "INSERT INTO Customers (FirstName, LastName, Email, IsActive, Balance, IsDeleted, CreatedAt) VALUES ('Ghost', 'Row', 'g@x.com', 1, 0, 0, '2026-01-01')");

        using var till = new TillDb();
        await till.AddCustomerAsync("Real");
        till.EnableSync();
        Assert.True((await till.Pusher(Transports.To(_cloud, site.Token)).RunOnceAsync(default)).Ok);

        Assert.Equal(0L, SqlHelper.Scalar<long>(MirrorPath(site), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'Ghost'"));
        Assert.Equal(1L, SqlHelper.Scalar<long>(MirrorPath(site), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'Real'"));
    }

    [Fact]
    public async Task A_resync_requested_in_the_portal_wipes_and_reuploads_on_the_next_push()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        await till.AddCustomerAsync("Keep");
        till.EnableSync();
        var pusher = till.Pusher(Transports.To(_cloud, site.Token));
        Assert.True((await pusher.RunOnceAsync(default)).Ok);

        // The mirror drifts (for example the till restored an older backup), then the admin asks for a resync.
        SqlHelper.Exec(MirrorPath(site),
            "INSERT INTO Customers (FirstName, LastName, Email, IsActive, Balance, IsDeleted, CreatedAt) VALUES ('Stale', 'Row', 's@x.com', 1, 0, 0, '2026-01-01')");
        await _cloud.WithControlDbAsync(async db =>
        {
            (await db.Sites.FirstAsync(s => s.Id == site.SiteId)).ResyncRequested = true;
            return await db.SaveChangesAsync();
        });

        await till.AddCustomerAsync("Trigger"); // any change makes the till talk to the cloud and hear the request
        var result = await pusher.RunOnceAsync(default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(0L, SqlHelper.Scalar<long>(MirrorPath(site), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'Stale'"));
        Assert.Equal(1L, SqlHelper.Scalar<long>(MirrorPath(site), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'Keep'"));
        Assert.Equal(1L, SqlHelper.Scalar<long>(MirrorPath(site), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'Trigger'"));

        var flag = await _cloud.WithControlDbAsync(db => db.Sites.AsNoTracking().Where(s => s.Id == site.SiteId).Select(s => s.ResyncRequested).FirstAsync());
        Assert.False(flag, "the request is consumed by the initial batch");
    }

    [Fact]
    public async Task An_outage_queues_changes_locally_and_the_next_run_delivers_them()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        till.EnableSync();
        var transport = new FlakyTransport(Transports.To(_cloud, site.Token), failAfterDelivery: false);
        var status = new SyncStatus();
        var pusher = till.Pusher(transport, status);

        var id = await till.AddCustomerAsync("Offline");
        var down = await pusher.RunOnceAsync(default);

        Assert.False(down.Ok);
        Assert.Equal("network", down.ErrorCode);
        Assert.True(down.Pending > 0);
        Assert.Equal("network", status.Snapshot().LastErrorCode);

        var up = await pusher.RunOnceAsync(default);
        Assert.True(up.Ok, up.Error);
        Assert.Equal(0, up.Pending);
        Assert.Equal("Offline", SqlHelper.Scalar<string>(MirrorPath(site), $"SELECT FirstName FROM Customers WHERE Id = {id}"));
        Assert.Null(status.Snapshot().LastError);
    }

    [Fact]
    public async Task A_lost_acknowledgement_resends_the_batch_without_duplicating_anything()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        till.EnableSync();
        var transport = new FlakyTransport(Transports.To(_cloud, site.Token), failAfterDelivery: true);
        var pusher = till.Pusher(transport);

        var id = await till.AddCustomerAsync("Twice");
        Assert.False((await pusher.RunOnceAsync(default)).Ok); // the cloud applied it, the till never heard
        Assert.True((await pusher.RunOnceAsync(default)).Ok);  // resent

        Assert.Equal(1L, SqlHelper.Scalar<long>(MirrorPath(site), $"SELECT COUNT(*) FROM Customers WHERE Id = {id}"));
        Assert.True(transport.Calls >= 2, "the batch must have been delivered twice");
    }

    [Fact]
    public async Task Large_backlogs_are_sent_in_several_batches_in_order()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        till.EnableSync();
        for (var i = 0; i < 12; i++) await till.AddCustomerAsync($"Bulk{i}");

        var result = await till.Pusher(Transports.To(_cloud, site.Token), batchSize: 5).RunOnceAsync(default);

        Assert.True(result.Ok, result.Error);
        Assert.True(result.BatchesSent >= 3);
        Assert.Equal(12L, SqlHelper.Scalar<long>(MirrorPath(site), "SELECT COUNT(*) FROM Customers WHERE FirstName LIKE 'Bulk%'"));
    }

    [Fact]
    public async Task Two_clients_never_see_each_others_data()
    {
        var a = await _cloud.SeedSiteAsync(clientName: "Shop A");
        var b = await _cloud.SeedSiteAsync(clientName: "Shop B");
        using var tillA = new TillDb();
        using var tillB = new TillDb();
        await tillA.AddCustomerAsync("OnlyInA");
        await tillB.AddCustomerAsync("OnlyInB");
        tillA.EnableSync();
        tillB.EnableSync();

        Assert.True((await tillA.Pusher(Transports.To(_cloud, a.Token)).RunOnceAsync(default)).Ok);
        Assert.True((await tillB.Pusher(Transports.To(_cloud, b.Token)).RunOnceAsync(default)).Ok);

        Assert.NotEqual(MirrorPath(a), MirrorPath(b));
        Assert.Equal(1L, SqlHelper.Scalar<long>(MirrorPath(a), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'OnlyInA'"));
        Assert.Equal(0L, SqlHelper.Scalar<long>(MirrorPath(a), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'OnlyInB'"));
        Assert.Equal(1L, SqlHelper.Scalar<long>(MirrorPath(b), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'OnlyInB'"));
        Assert.Equal(0L, SqlHelper.Scalar<long>(MirrorPath(b), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'OnlyInA'"));
    }

    [Fact]
    public async Task An_expired_subscription_pauses_sync_and_keeps_the_changes_queued_until_renewal()
    {
        var site = await _cloud.SeedSiteAsync(subscriptionMonths: 1);
        using var till = new TillDb();
        till.EnableSync();
        var pusher = till.Pusher(Transports.To(_cloud, site.Token));
        Assert.True((await pusher.RunOnceAsync(default)).Ok);

        var original = _cloud.Clock.Now;
        try
        {
            _cloud.Clock.Now = original.AddMonths(1).AddDays(SubscriptionRulesGrace + 1);
            var id = await till.AddCustomerAsync("WhileExpired");
            var blocked = await pusher.RunOnceAsync(default);
            Assert.False(blocked.Ok);
            Assert.Equal("subscription_inactive", blocked.ErrorCode);
            Assert.True(blocked.Pending > 0);
            Assert.Equal(0L, SqlHelper.Scalar<long>(MirrorPath(site), $"SELECT COUNT(*) FROM Customers WHERE Id = {id}"));

            // Renewal in the portal: the queued change goes through, nothing was lost.
            await _cloud.WithControlDbAsync(async db =>
            {
                var sub = await db.Subscriptions.FirstAsync(s => s.ClientId == site.ClientId);
                sub.ExpiresAt = _cloud.Clock.GetUtcNow().UtcDateTime.AddMonths(12);
                return await db.SaveChangesAsync();
            });
            var resumed = await pusher.RunOnceAsync(default);
            Assert.True(resumed.Ok, resumed.Error);
            Assert.Equal(1L, SqlHelper.Scalar<long>(MirrorPath(site), $"SELECT COUNT(*) FROM Customers WHERE Id = {id}"));
        }
        finally
        {
            _cloud.Clock.Now = original;
        }
    }

    private static int SubscriptionRulesGrace => Cloud.Services.SubscriptionRules.GraceDays;
}
