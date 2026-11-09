using System.Net;
using System.Text.Json;
using Application.IServices.Logging;
using Core.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Sync;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Web.Api.Online;
using Xunit;

namespace Tests.Api.Online;

/// <summary>
/// Private Online on the shop PC: the recovery slip behind the owner's PIN (show, print, PDF), the confirm step and the restore of a
/// key on a new PC. The sync itself (what is sealed, what the cloud keeps) is covered in Cloud.Tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OnlineSealingApiTests : CatalogApiTestBase
{
    public OnlineSealingApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static HttpRequestMessage WithPin(string url, string pin, object? more = null)
        => more is null
            ? Json(HttpMethod.Post, url, new { pin })
            : Json(HttpMethod.Post, url, new { pin, code = more });

    private static readonly string[] PinEndpoints =
    [
        "/api/v1/online/sealing/slip", "/api/v1/online/sealing/slip/print", "/api/v1/online/sealing/slip/pdf",
    ];

    [Fact]
    public async Task Without_a_cookie_every_sealing_action_answers_401()
    {
        var anonymous = Factory.CreateBrowser();

        foreach (var url in PinEndpoints)
            await AssertProblem(await anonymous.SendAsync(WithPin(url, "1234")), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.SendAsync(Json(HttpMethod.Post, "/api/v1/online/sealing/confirm", new { })), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.SendAsync(WithPin("/api/v1/online/sealing/restore", "1234", "x")), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_cannot_see_the_recovery_code_even_with_their_own_pin()
    {
        var cashier = await Cashier();

        foreach (var url in PinEndpoints)
            await AssertProblem(await cashier.SendAsync(WithPin(url, ApiFactory.CashierPin)), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/online/sealing/confirm", new { })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(WithPin("/api/v1/online/sealing/restore", ApiFactory.CashierPin, "x")), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task The_status_says_which_step_the_shop_is_at_and_never_carries_the_key()
    {
        var admin = await Admin();

        var response = await admin.GetAsync("/api/v1/online");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await Body(response);
        Assert.Equal(SealingStates.Off, status.GetProperty("sealingState").GetString()); // never connected in these tests
        Assert.DoesNotContain("code", (await response.Content.ReadAsStringAsync()).Replace("lastErrorCode", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_wrong_pin_is_refused_as_a_pin_error_and_shows_nothing()
    {
        var admin = await Admin();

        foreach (var url in PinEndpoints)
        {
            var response = await admin.SendAsync(WithPin(url, "0000"));
            await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"pin\"", body);
            Assert.DoesNotContain("groups", body);
        }
    }

    [Fact]
    public async Task The_right_pin_shows_the_code_for_the_key_on_this_pc_and_it_is_stable()
    {
        var admin = await Admin();

        var first = await Body(await admin.SendAsync(WithPin("/api/v1/online/sealing/slip", ApiFactory.AdminPin)));
        var again = await Body(await admin.SendAsync(WithPin("/api/v1/online/sealing/slip", ApiFactory.AdminPin)));

        Assert.Equal(first.GetProperty("code").GetString(), again.GetProperty("code").GetString());
        Assert.Equal(14, first.GetProperty("groups").GetArrayLength());
        Assert.True(RecoveryCode.TryParse(first.GetProperty("code").GetString(), out var key));
        Assert.Equal(new RowSealer(key).KeyId, first.GetProperty("keyId").GetString());

        // The status now shows the same public fingerprint, and still not the key.
        var status = await Body(await admin.GetAsync("/api/v1/online"));
        Assert.Equal(first.GetProperty("keyId").GetString(), status.GetProperty("sealKeyId").GetString());
    }

    [Fact]
    public async Task The_slip_can_be_printed_or_saved_as_a_pdf()
    {
        var admin = await Admin();

        var print = await admin.SendAsync(WithPin("/api/v1/online/sealing/slip/print", ApiFactory.AdminPin));
        Assert.Equal(HttpStatusCode.OK, print.StatusCode);
        var printed = await Body(print);
        Assert.False(string.IsNullOrWhiteSpace(printed.GetProperty("message").GetString())); // printed or not, the owner is told

        var pdf = await admin.SendAsync(WithPin("/api/v1/online/sealing/slip/pdf", ApiFactory.AdminPin));
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 500);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task Confirming_without_a_connection_is_a_conflict_and_changes_nothing()
    {
        var admin = await Admin();

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/online/sealing/confirm", new { })), HttpStatusCode.Conflict, "not_connected");

        Assert.Equal(SealingStates.Off, (await Body(await admin.GetAsync("/api/v1/online"))).GetProperty("sealingState").GetString());
    }

    [Fact]
    public async Task An_incomplete_recovery_code_is_refused_and_a_pin_is_still_needed()
    {
        var admin = await Admin();

        await AssertProblem(await admin.SendAsync(WithPin("/api/v1/online/sealing/restore", ApiFactory.AdminPin, "K7QM-2XPA")), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.SendAsync(WithPin("/api/v1/online/sealing/restore", "0000", "K7QM-2XPA")), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task A_pc_that_already_has_a_key_refuses_a_different_one()
    {
        var admin = await Admin();
        await admin.SendAsync(WithPin("/api/v1/online/sealing/slip", ApiFactory.AdminPin)); // makes sure this PC has a key
        var other = RecoveryCode.Format(RowSealer.NewKey());

        await AssertProblem(await admin.SendAsync(WithPin("/api/v1/online/sealing/restore", ApiFactory.AdminPin, other)), HttpStatusCode.Conflict, "key_exists");

        // And the same key again is accepted (a PC being restored twice is no harm).
        var mine = (await Body(await admin.SendAsync(WithPin("/api/v1/online/sealing/slip", ApiFactory.AdminPin)))).GetProperty("code").GetString();
        var response = await admin.SendAsync(WithPin("/api/v1/online/sealing/restore", ApiFactory.AdminPin, mine));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_pins_lock_the_check_and_the_audit_trail_has_the_events_but_never_the_code()
    {
        // A user id that does not exist: the lock is per user, so the admin's own attempts in the other tests are untouched.
        using var scope = Factory.Services.CreateScope();
        var sealing = scope.ServiceProvider.GetRequiredService<IOnlineSealingApiService>();
        const int stranger = 987654;

        for (var i = 0; i < 5; i++)
            Assert.Equal("invalid_pin", (await Assert.ThrowsAsync<OnlineRuleException>(() => sealing.GetSlipAsync(stranger, "0000"))).Code);
        var locked = await Assert.ThrowsAsync<OnlineRuleException>(() => sealing.GetSlipAsync(stranger, "0000"));
        Assert.Equal("pin_locked", locked.Code);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, locked.Status);

        // The admin shows the code once; the trail records that, with the key's id, and not the code.
        var admin = await Admin();
        var code = (await Body(await admin.SendAsync(WithPin("/api/v1/online/sealing/slip", ApiFactory.AdminPin)))).GetProperty("code").GetString()!;

        await scope.ServiceProvider.GetRequiredService<IAuditTrail>().FlushAsync();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var (shown, _) = await audit.SearchAsync(new AuditLogFilter { Action = AuditActions.RecoverySlipShown }, 0, 50);
        var (failed, _) = await audit.SearchAsync(new AuditLogFilter { Action = AuditActions.RecoverySlipFailed }, 0, 50);
        Assert.NotEmpty(shown);
        Assert.NotEmpty(failed);

        var everything = JsonSerializer.Serialize(shown.Concat(failed));
        Assert.DoesNotContain(code, everything);
        foreach (var group in code.Split('-')) Assert.DoesNotContain(group + "-", everything); // not even in pieces
    }
}
