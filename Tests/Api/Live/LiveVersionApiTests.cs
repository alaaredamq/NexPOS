using System.Net;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.Live;

/// <summary>
/// Several PCs share one store database, so a till left open on the POS has to learn that another till sold. The server moves a version
/// number when a write succeeds; the open screens poll it. These tests pin that contract: reads never move it, a successful write does,
/// a refused or failed write does not, and the answer is not cached.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LiveVersionApiTests : CatalogApiTestBase
{
    public LiveVersionApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static async Task<string> VersionAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/live/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Body(response)).GetProperty("version").GetString()!;
    }

    [Fact]
    public async Task Only_a_signed_in_user_can_ask()
    {
        await AssertProblem(await Factory.CreateBrowser().GetAsync("/api/v1/live/version"), HttpStatusCode.Unauthorized, "unauthenticated");
        Assert.False(string.IsNullOrEmpty(await VersionAsync(await Cashier())));
    }

    [Fact]
    public async Task Polling_does_not_move_the_version_and_is_never_cached()
    {
        var cashier = await Cashier();
        var first = await VersionAsync(cashier);

        for (var i = 0; i < 3; i++) Assert.Equal(first, await VersionAsync(cashier));
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/notifications/feed")).StatusCode); // another read

        var response = await cashier.GetAsync("/api/v1/live/version");
        Assert.Equal(first, (await Body(response)).GetProperty("version").GetString());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_write_by_one_user_is_seen_by_another()
    {
        var till2 = await Cashier();
        var till1 = await Admin();
        var before = await VersionAsync(till2);

        // Any successful write counts; marking a notification read is the cheapest one that needs no setup.
        var write = await till1.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/read-all"));
        Assert.True(write.IsSuccessStatusCode, $"setup write failed: {(int)write.StatusCode}");

        Assert.NotEqual(before, await VersionAsync(till2));
    }

    [Fact]
    public async Task A_refused_write_does_not_move_it()
    {
        var cashier = await Cashier();
        var before = await VersionAsync(cashier);

        // The cashier has no Alerts.Update: 403, nothing changed.
        var refused = await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/alerts/dismiss-all"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        Assert.Equal(before, await VersionAsync(cashier));
    }
}
