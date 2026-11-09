using System.Net;
using System.Net.Http.Json;
using Cloud.Tests.Support;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>What the provider's portal says about how private each branch's cloud copy is.</summary>
public class PrivacyPortalTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public PrivacyPortalTests(CloudFactory cloud) => _cloud = cloud;

    private async Task<HttpClient> SignedInAsync()
    {
        var c = _cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var response = await HtmlForms.PostAsync(c, "/Account/Login", "/Account/Login",
            ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return c;
    }

    private async Task<(SeededSite Site, TillDb Till)> UploadedAsync(RowSealer? sealer)
    {
        var site = await _cloud.SeedSiteAsync();
        var till = new TillDb();
        await till.AddCustomerAsync("Zainab");
        await till.AddProductAsync("Olive oil 1L", "6001");
        till.EnableSync();
        Assert.True((await till.Pusher(Transports.To(_cloud, site.Token), sealer: sealer).RunOnceAsync(default)).Ok);
        return (site, till);
    }

    [Fact]
    public async Task A_sealed_branch_says_so_with_the_key_fingerprint_and_that_the_cloud_cannot_read_it()
    {
        var c = await SignedInAsync();
        var sealer = new RowSealer(RowSealer.NewKey());
        var (site, till) = await UploadedAsync(sealer);
        using var _ = till;

        var page = await c.GetStringAsync($"/Sites/Details/{site.SiteId}");

        Assert.Contains("<dt>Privacy</dt>", page);
        Assert.Contains(">Sealed</span>", page);
        Assert.Contains($"key {sealer.KeyId[..8]}", page);
        Assert.Contains("cannot read them", page);
        Assert.DoesNotContain(sealer.KeyId, page); // only the short prefix is shown, and no key material ever
        Assert.DoesNotContain("Zainab", page);
    }

    [Fact]
    public async Task A_branch_that_uploads_readable_is_flagged_as_readable()
    {
        var c = await SignedInAsync();
        var (site, till) = await UploadedAsync(sealer: null);
        using var _ = till;

        var page = await c.GetStringAsync($"/Sites/Details/{site.SiteId}");

        Assert.Contains(">Readable</span>", page);
        Assert.Contains("can be read on this server", page);
        Assert.DoesNotContain(">Sealed</span>", page);
    }

    [Fact]
    public async Task A_branch_that_never_uploaded_says_nothing_about_privacy()
    {
        var c = await SignedInAsync();
        var site = await _cloud.SeedSiteAsync();

        var page = await c.GetStringAsync($"/Sites/Details/{site.SiteId}");

        Assert.DoesNotContain("<dt>Privacy</dt>", page);
    }

    [Fact]
    public async Task While_a_branch_switches_the_portal_says_it_is_switching()
    {
        var c = await SignedInAsync();
        var (site, till) = await UploadedAsync(sealer: null); // readable first
        using var _ = till;

        // The shop sends a sealed row while its readable ones are still there: what a half-finished switch looks like.
        var blob = new RowSealer(RowSealer.NewKey()).Seal("Customers", 900, new Dictionary<string, object?> { ["FirstName"] = "x" });
        var response = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("/api/sync/push",
            new SyncBatch(_cloud.Mirrors.LatestMigration, 0, 1, false, [new SyncChange("Customers", 900, false, null, blob)], SyncFormat.Sealed),
            SyncWire.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(SqlHelper.Scalar<long>(_cloud.Mirrors.PathFor(site.SiteId), "SELECT COUNT(*) FROM Customers WHERE Id <> 900") > 0);

        var page = await c.GetStringAsync($"/Sites/Details/{site.SiteId}");

        Assert.Contains(">Switching</span>", page);
    }
}
