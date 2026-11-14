using System.Net;
using System.Net.Http.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sync;
using Xunit;

namespace Cloud.Tests;

public class EnrollmentApiTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public EnrollmentApiTests(CloudFactory cloud) => _cloud = cloud;

    private static string CodeOf(string link) => link[(link.LastIndexOf('/') + 1)..];

    private Task<HttpResponseMessage> Redeem(HttpClient client, string code, string? machineId = null)
        => client.PostAsJsonAsync("/api/enroll", new EnrollRequest(code, machineId, "TILL-PC"), SyncWire.Json);

    private static async Task<SyncError?> ErrorOf(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<SyncError>(SyncWire.Json);

    [Fact]
    public async Task A_valid_code_returns_a_working_token_and_records_the_machine()
    {
        var site = await _cloud.SeedSiteAsync(clientName: "Enroll Shop");
        var code = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));

        var response = await Redeem(_cloud.CreateClient(), code, "ABCDE-12345-ABCDE-12345");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var enrolled = (await response.Content.ReadFromJsonAsync<EnrollResponse>(SyncWire.Json))!;
        Assert.StartsWith("nxp_", enrolled.Token);
        Assert.Equal("Main till", enrolled.SiteName);
        Assert.Equal("Enroll Shop", enrolled.ClientName);

        var ping = await _cloud.DeviceClient(enrolled.Token).GetAsync("/api/sync/ping");
        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);

        var stored = await _cloud.WithControlDbAsync(db => db.Sites.AsNoTracking().FirstAsync(s => s.Id == site.SiteId));
        Assert.Equal("ABCDE-12345-ABCDE-12345", stored.MachineId);
        var events = await _cloud.WithControlDbAsync(db => db.ClientEvents.AsNoTracking().Where(e => e.ClientId == site.ClientId).ToListAsync());
        Assert.Contains(events, e => e.Text.Contains("Branch connected"));
    }

    [Fact]
    public async Task The_code_is_single_use()
    {
        var site = await _cloud.SeedSiteAsync();
        var code = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));
        var client = _cloud.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await Redeem(client, code)).StatusCode);
        var again = await Redeem(client, code);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(SyncErrorCodes.EnrollCodeUsed, (await ErrorOf(again))!.Code);
    }

    [Fact]
    public async Task Two_simultaneous_redemptions_produce_exactly_one_token()
    {
        var site = await _cloud.SeedSiteAsync();
        var code = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Redeem(_cloud.CreateClient(), code)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        // The one winner replaced the seeded token: exactly one credential is live, never two.
        var active = await _cloud.WithControlDbAsync(db => db.DeviceTokens.CountAsync(t => t.SiteId == site.SiteId && t.RevokedAt == null));
        Assert.Equal(1, active);
    }

    [Fact]
    public async Task Codes_ignore_case_dashes_and_spaces_so_they_survive_being_retyped()
    {
        var site = await _cloud.SeedSiteAsync();
        var code = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));
        var sloppy = " " + code.ToLowerInvariant().Replace('-', ' ') + " ";

        Assert.Equal(HttpStatusCode.OK, (await Redeem(_cloud.CreateClient(), sloppy)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("AAAA-AAAA-AAAA")] // well-formed, never issued
    [InlineData("0000-0000-0000")] // characters outside the alphabet
    public async Task Unknown_or_malformed_codes_are_refused(string code)
    {
        using var factory = new CloudFactory(); // own throttle: this test must not eat the shared one
        var response = await Redeem(factory.CreateClient(), code);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SyncErrorCodes.EnrollCodeInvalid, (await ErrorOf(response))!.Code);
    }

    [Fact]
    public async Task Codes_expire_after_a_week()
    {
        var site = await _cloud.SeedSiteAsync();
        var code = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));
        var original = _cloud.Clock.Now;
        try
        {
            _cloud.Clock.Now = original.AddDays(7).AddMinutes(1);
            var response = await Redeem(_cloud.CreateClient(), code);
            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            Assert.Equal(SyncErrorCodes.EnrollCodeExpired, (await ErrorOf(response))!.Code);
        }
        finally
        {
            _cloud.Clock.Now = original;
        }
    }

    [Fact]
    public async Task Making_a_new_link_cancels_the_previous_one()
    {
        var site = await _cloud.SeedSiteAsync();
        var first = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));
        var second = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));
        var client = _cloud.CreateClient();

        Assert.Equal(HttpStatusCode.Gone, (await Redeem(client, first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Redeem(client, second)).StatusCode);
    }

    [Fact]
    public async Task Enrolling_again_replaces_the_old_credential()
    {
        var site = await _cloud.SeedSiteAsync();
        var oldClient = _cloud.DeviceClient(site.Token);
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync("/api/sync/ping")).StatusCode);

        var response = await Redeem(_cloud.CreateClient(), CodeOf(await _cloud.IssueLinkAsync(site.SiteId)));
        var fresh = (await response.Content.ReadFromJsonAsync<EnrollResponse>(SyncWire.Json))!.Token;

        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/sync/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cloud.DeviceClient(fresh).GetAsync("/api/sync/ping")).StatusCode);
    }

    [Fact]
    public async Task A_disabled_installation_or_client_cannot_enrol_and_the_code_survives()
    {
        var site = await _cloud.SeedSiteAsync();
        var code = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));
        await _cloud.WithControlDbAsync(async db =>
        {
            (await db.Sites.FirstAsync(s => s.Id == site.SiteId)).IsDisabled = true;
            return await db.SaveChangesAsync();
        });

        var blocked = await Redeem(_cloud.CreateClient(), code);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(SyncErrorCodes.SiteDisabled, (await ErrorOf(blocked))!.Code);

        await _cloud.WithControlDbAsync(async db =>
        {
            (await db.Sites.FirstAsync(s => s.Id == site.SiteId)).IsDisabled = false;
            return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await Redeem(_cloud.CreateClient(), code)).StatusCode);
    }

    [Fact]
    public async Task Cancelling_open_codes_stops_a_link_that_was_sent_to_the_wrong_person()
    {
        var site = await _cloud.SeedSiteAsync();
        var code = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));
        using (var scope = _cloud.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<EnrollmentService>().CancelOpenCodesAsync(site.SiteId);

        Assert.Equal(HttpStatusCode.Gone, (await Redeem(_cloud.CreateClient(), code)).StatusCode);
    }

    [Fact]
    public async Task Guessing_is_throttled_even_for_a_later_correct_code()
    {
        using var factory = new CloudFactory();
        var site = await factory.SeedSiteAsync();
        var code = CodeOf(await factory.IssueLinkAsync(site.SiteId));
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++) await Redeem(client, "AAAA-AAAA-AAAA");

        var blocked = await Redeem(client, code);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(SyncErrorCodes.RateLimited, (await ErrorOf(blocked))!.Code);
    }

    [Fact]
    public async Task Codes_are_stored_only_as_hashes()
    {
        var site = await _cloud.SeedSiteAsync();
        var code = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));
        var stored = await _cloud.WithControlDbAsync(db => db.EnrollmentCodes.AsNoTracking().Where(c => c.SiteId == site.SiteId).ToListAsync());

        Assert.Single(stored);
        Assert.DoesNotContain(code.Replace("-", ""), stored[0].CodeHash);
        Assert.Equal(EnrollmentCodes.Hash(code.Replace("-", "")), stored[0].CodeHash);
    }

    [Fact]
    public async Task The_public_enrol_page_opens_without_signing_in_and_never_looks_the_code_up()
    {
        var site = await _cloud.SeedSiteAsync();
        var code = CodeOf(await _cloud.IssueLinkAsync(site.SiteId));
        var page = await _cloud.CreateClient(new() { AllowAutoRedirect = false }).GetAsync($"/enroll/{code}");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Settings", html);
        Assert.DoesNotContain(code, html);
        Assert.DoesNotContain("Corner Shop", html);

        // Looking at the page did not use the code up.
        Assert.Equal(HttpStatusCode.OK, (await Redeem(_cloud.CreateClient(), code)).StatusCode);
    }
}
