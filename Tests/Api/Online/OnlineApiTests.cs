using System.Net;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.Online;

/// <summary>
/// Settings &gt; Online API: who may use it, what a till that was never connected reports, and how a refused link is answered.
/// The cloud itself is covered in Cloud.Tests; here the host must simply behave when Online is off (the default) and
/// when a link is wrong or unreachable, without touching the network for anything but the connect attempt.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OnlineApiTests : CatalogApiTestBase
{
    public OnlineApiTests(ApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Without_a_cookie_every_online_action_answers_401()
    {
        var anonymous = Factory.CreateBrowser();

        await AssertProblem(await anonymous.GetAsync("/api/v1/online"), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.SendAsync(Json(HttpMethod.Post, "/api/v1/online/connect", new { link = "x" })), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.SendAsync(Json(HttpMethod.Post, "/api/v1/online/disconnect", new { })), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.SendAsync(Json(HttpMethod.Post, "/api/v1/online/sync-now", new { })), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.GetAsync("/api/v1/online/recovery-code"), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_cannot_read_the_recovery_code()
    {
        var cashier = await Cashier();

        await AssertProblem(await cashier.GetAsync("/api/v1/online/recovery-code"), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task There_is_no_recovery_code_before_the_till_has_connected_and_the_status_never_carries_the_key()
    {
        var admin = await Admin();

        await AssertProblem(await admin.GetAsync("/api/v1/online/recovery-code"), HttpStatusCode.NotFound, "no_recovery_code");

        var text = await (await admin.GetAsync("/api/v1/online")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("key", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recovery", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_cashier_cannot_see_or_change_the_online_connection()
    {
        var cashier = await Cashier();

        await AssertProblem(await cashier.GetAsync("/api/v1/online"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/online/connect", new { link = "x" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/online/disconnect", new { })), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_till_that_was_never_connected_reports_it_and_carries_no_secrets()
    {
        var admin = await Admin();

        var response = await admin.GetAsync("/api/v1/online");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var status = (await Body(await admin.GetAsync("/api/v1/online")));
        Assert.False(status.GetProperty("connected").GetBoolean());
        Assert.Equal(0, status.GetProperty("pendingChanges").GetInt64());
        Assert.DoesNotContain("deviceToken", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("this is not a link", "enroll_link_invalid", HttpStatusCode.BadRequest)]
    [InlineData("http://cloud.example.com/enroll/AAAA-AAAA-AAAA", "cloud_insecure_url", HttpStatusCode.BadRequest)]
    [InlineData("https://nexpos-cloud.invalid/enroll/AAAA-AAAA-AAAA", "cloud_unreachable", HttpStatusCode.BadGateway)]
    public async Task A_refused_link_answers_with_a_stable_code_and_connects_nothing(string link, string code, HttpStatusCode status)
    {
        var admin = await Admin();

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/online/connect", new { link })), status, code);

        Assert.False((await Body(await admin.GetAsync("/api/v1/online"))).GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task An_empty_link_is_a_validation_error()
    {
        var admin = await Admin();

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/online/connect", new { link = "" })), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Disconnect_and_sync_now_are_harmless_on_a_till_that_was_never_connected()
    {
        var admin = await Admin();

        var sync = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/online/sync-now", new { }));
        var disconnect = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/online/disconnect", new { }));

        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        Assert.False((await Body(disconnect)).GetProperty("connected").GetBoolean());
    }
}
