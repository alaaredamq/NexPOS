using System.Net;
using System.Net.Http.Json;
using Cloud.Data;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Sync;
using Xunit;

namespace Cloud.Tests;

public class SyncApiTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public SyncApiTests(CloudFactory cloud) => _cloud = cloud;

    private string Latest => _cloud.Mirrors.LatestMigration;

    private SyncBatch Batch(params SyncChange[] changes)
        => new(Latest, 0, 1, false, changes);

    private static SyncChange CustomerRow(int id, string name) => new("Customers", id, false, new Dictionary<string, object?>
    {
        ["FirstName"] = name, ["LastName"] = "X", ["Email"] = $"{name}@x.com", ["IsActive"] = 1L, ["Balance"] = "0",
        ["IsDeleted"] = 0L, ["CreatedAt"] = "2026-01-01 00:00:00",
    });

    private static async Task<SyncError?> ErrorOf(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<SyncError>(SyncWire.Json);

    [Fact]
    public async Task Push_without_a_token_is_refused()
    {
        var response = await _cloud.CreateClient().PostAsJsonAsync("/api/sync/push", Batch(), SyncWire.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SyncErrorCodes.Unauthorized, (await ErrorOf(response))!.Code);
    }

    [Fact]
    public async Task Push_with_an_unknown_token_is_refused_and_applies_nothing()
    {
        var response = await _cloud.DeviceClient("nxp_not-a-real-token").PostAsJsonAsync("/api/sync/push", Batch(CustomerRow(1, "Eve")), SyncWire.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_admin_cookie_is_not_a_device_credential_and_a_device_token_does_not_open_the_portal()
    {
        var site = await _cloud.SeedSiteAsync();
        var noRedirect = _cloud.CreateClient(new() { AllowAutoRedirect = false });
        noRedirect.DefaultRequestHeaders.Authorization = new("Bearer", site.Token);

        var portal = await noRedirect.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, portal.StatusCode);
        Assert.Contains("/Account/Login", portal.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Ping_reports_the_site_and_plan()
    {
        var site = await _cloud.SeedSiteAsync(clientName: "Ping Shop");
        var ping = await _cloud.DeviceClient(site.Token).GetFromJsonAsync<SyncPing>("/api/sync/ping", SyncWire.Json);
        Assert.Equal("Ping Shop", ping!.ClientName);
        Assert.Equal("Active", ping.SubscriptionStatus);
    }

    [Fact]
    public async Task A_revoked_token_stops_working_immediately()
    {
        var site = await _cloud.SeedSiteAsync();
        var client = _cloud.DeviceClient(site.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sync/ping")).StatusCode);

        await _cloud.WithControlDbAsync(async db =>
        {
            foreach (var t in db.DeviceTokens.Where(t => t.SiteId == site.SiteId)) t.RevokedAt = DateTime.UtcNow;
            return await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sync/ping")).StatusCode);
    }

    [Fact]
    public async Task A_disabled_installation_or_client_is_refused()
    {
        var site = await _cloud.SeedSiteAsync();
        var client = _cloud.DeviceClient(site.Token);

        await _cloud.WithControlDbAsync(async db =>
        {
            (await db.Sites.FirstAsync(s => s.Id == site.SiteId)).IsDisabled = true;
            return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sync/ping")).StatusCode);

        await _cloud.WithControlDbAsync(async db =>
        {
            (await db.Sites.FirstAsync(s => s.Id == site.SiteId)).IsDisabled = false;
            (await db.Clients.FirstAsync(c => c.Id == site.ClientId)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sync/ping")).StatusCode);
    }

    [Fact]
    public async Task Grace_week_still_accepts_data_then_402_and_suspension_is_immediate()
    {
        var site = await _cloud.SeedSiteAsync(subscriptionMonths: 1);
        var client = _cloud.DeviceClient(site.Token);
        var expires = _cloud.Clock.Now.AddMonths(1);
        var original = _cloud.Clock.Now;
        try
        {
            _cloud.Clock.Now = expires.AddDays(3);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/sync/push", Batch(CustomerRow(1, "Grace")), SyncWire.Json)).StatusCode);

            _cloud.Clock.Now = expires.AddDays(8);
            var late = await client.PostAsJsonAsync("/api/sync/push", Batch(CustomerRow(2, "TooLate")), SyncWire.Json);
            Assert.Equal(HttpStatusCode.PaymentRequired, late.StatusCode);
            Assert.Equal(SyncErrorCodes.SubscriptionInactive, (await ErrorOf(late))!.Code);

            _cloud.Clock.Now = original;
            await _cloud.WithControlDbAsync(async db =>
            {
                (await db.Subscriptions.FirstAsync(s => s.ClientId == site.ClientId)).IsSuspended = true;
                return await db.SaveChangesAsync();
            });
            Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/api/sync/push", Batch(), SyncWire.Json)).StatusCode);
        }
        finally
        {
            _cloud.Clock.Now = original;
        }
        Assert.Equal(0L, SqlHelper.Scalar<long>(_cloud.Mirrors.PathFor(site.SiteId), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'TooLate'"));
    }

    [Fact]
    public async Task A_client_without_any_subscription_cannot_sync()
    {
        var site = await _cloud.SeedSiteAsync(subscriptionMonths: 0);
        var response = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("/api/sync/push", Batch(), SyncWire.Json);
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task A_till_newer_than_the_cloud_is_told_to_wait_for_a_cloud_upgrade()
    {
        var site = await _cloud.SeedSiteAsync();
        var newer = new SyncBatch("99991231000000_FromTheFuture", 0, 1, false, []);
        var response = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("/api/sync/push", newer, SyncWire.Json);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SyncErrorCodes.SchemaNewer, (await ErrorOf(response))!.Code);
    }

    [Fact]
    public async Task A_batch_naming_a_table_outside_the_whitelist_is_rejected_whole_and_logged()
    {
        var site = await _cloud.SeedSiteAsync();
        var client = _cloud.DeviceClient(site.Token);
        var evil = Batch(
            CustomerRow(1, "Collateral"),
            new SyncChange("sqlite_master", 1, true, null),
            new SyncChange("Users\"; DROP TABLE Customers; --", 1, true, null));

        var response = await client.PostAsJsonAsync("/api/sync/push", evil, SyncWire.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.BadBatch, (await ErrorOf(response))!.Code);
        // All or nothing: the valid first row did not slip in, and the table is intact.
        _cloud.Mirrors.EnsureReady(site.SiteId);
        Assert.Equal(0L, SqlHelper.Scalar<long>(_cloud.Mirrors.PathFor(site.SiteId), "SELECT COUNT(*) FROM Customers WHERE FirstName = 'Collateral'"));
        var log = await _cloud.WithControlDbAsync(db => db.SyncBatchLogs.AsNoTracking().Where(l => l.SiteId == site.SiteId).ToListAsync());
        Assert.Contains(log, l => l.Outcome == "rejected");
    }

    [Fact]
    public async Task Columns_outside_the_synced_set_are_ignored_so_credentials_cannot_be_smuggled_in()
    {
        var site = await _cloud.SeedSiteAsync();
        var row = new SyncChange("Users", 9001, false, new Dictionary<string, object?>
        {
            ["Username"] = "mallory", ["Email"] = "m@x.com", ["IsActive"] = 1L, ["RoleId"] = 1L,
            ["PasswordHash"] = "attacker-chosen", ["MustChangePassword"] = 0L, ["MustChangePin"] = 0L, ["IsDeleted"] = 0L,
            ["CreatedAt"] = "2026-01-01 00:00:00",
        });
        var response = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("/api/sync/push", Batch(row), SyncWire.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", SqlHelper.Scalar<string>(_cloud.Mirrors.PathFor(site.SiteId), "SELECT PasswordHash FROM Users WHERE Id = 9001"));
    }

    [Fact]
    public async Task Oversized_batches_are_refused()
    {
        var site = await _cloud.SeedSiteAsync();
        var many = Enumerable.Range(1, 5001).Select(i => new SyncChange("Customers", i, true, null)).ToArray();
        var response = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("/api/sync/push", Batch(many), SyncWire.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await _cloud.CreateClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"controlDb\":\"sqlite\"", body); // tests run on SQLite; the connection string itself is never shown
    }
}
