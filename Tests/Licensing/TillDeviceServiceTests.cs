using Application.DTOs.Licensing;
using Application.Services.Licensing;
using Common.Licensing;
using Microsoft.EntityFrameworkCore;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Licensing;

/// <summary>The registered tills behind a multi-seat licence: seat counting, pairing codes, and who may sell.</summary>
public class TillDeviceServiceTests : IDisposable
{
    private static readonly DateOnly IssuedOn = new(2026, 10, 1);
    private static readonly DateOnly Until = new(2027, 10, 1);

    private readonly SqliteInMemoryDbContextFactory _dbs = new();
    private readonly LicenseTestIssuer _issuer = new();
    private readonly InMemoryLicenseStorage _storage = new();
    private readonly MutableClock _clock = new();
    private readonly PairingThrottle _throttle = new();

    public void Dispose()
    {
        _dbs.Dispose();
        _issuer.Dispose();
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private void InstallKey(int seats) =>
        _storage.Text = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, IssuedOn, Until, seats: seats);

    /// <summary>A service over the shared database, reading whatever key is in storage right now (a new LicenseService each time, like a restart).</summary>
    private TillDeviceService Sut(bool enforce = true)
    {
        var license = LicenseServiceFactory.Create(_issuer, _storage, new FakeClock(IssuedOn.AddDays(30)), enforce: enforce);
        return new TillDeviceService(_dbs.CreateContext(), license, _throttle, _clock);
    }

    private async Task<string> AddTill(TillDeviceService sut, string name = "Till")
    {
        var issued = await sut.IssuePairingCodeAsync(name);
        Assert.Equal(IssuePairingOutcome.Success, issued.Outcome);
        var paired = await sut.PairAsync(issued.Code, "10.0.0.5");
        Assert.Equal(PairOutcome.Success, paired.Outcome);
        return paired.Token!;
    }

    // ---- seat counting -----------------------------------------------------------------------

    [Fact]
    public async Task A_three_pc_package_has_two_till_seats_besides_the_server()
    {
        InstallKey(3);
        var sut = Sut();

        Assert.Equal(IssuePairingOutcome.Success, (await sut.IssuePairingCodeAsync("Till 2")).Outcome);
        Assert.Equal(IssuePairingOutcome.Success, (await sut.IssuePairingCodeAsync("Till 3")).Outcome);
        Assert.Equal(IssuePairingOutcome.SeatsFull, (await sut.IssuePairingCodeAsync("Till 4")).Outcome);

        var seats = await sut.GetSeatsAsync();
        Assert.Equal(3, seats.Seats);
        Assert.Equal(2, seats.TillSeats);
        Assert.Equal(2, seats.Used);
    }

    [Fact]
    public async Task A_single_pc_licence_has_no_till_seats()
    {
        InstallKey(1);

        Assert.Equal(IssuePairingOutcome.SeatsFull, (await Sut().IssuePairingCodeAsync("Till 2")).Outcome);
    }

    [Fact]
    public async Task Nothing_can_be_added_while_the_licence_is_not_active()
    {
        var sut = Sut(); // no key installed

        Assert.Equal(IssuePairingOutcome.LicenseInactive, (await sut.IssuePairingCodeAsync("Till 2")).Outcome);
        Assert.Equal(PairOutcome.LicenseInactive, (await sut.PairAsync("ABCD-2345", "10.0.0.5")).Outcome);
    }

    [Fact]
    public async Task A_build_that_does_not_enforce_licences_has_no_seat_limit()
    {
        var sut = Sut(enforce: false);

        for (var i = 0; i < 6; i++) Assert.Equal(IssuePairingOutcome.Success, (await sut.IssuePairingCodeAsync($"Till {i}")).Outcome);

        var seats = await sut.GetSeatsAsync();
        Assert.False(seats.Enforced);
        Assert.Equal(int.MaxValue, seats.TillSeats);
    }

    [Fact]
    public async Task A_till_needs_a_name()
    {
        InstallKey(3);

        Assert.Equal(IssuePairingOutcome.NameRequired, (await Sut().IssuePairingCodeAsync("   ")).Outcome);
    }

    // ---- pairing -----------------------------------------------------------------------------

    [Fact]
    public async Task A_paired_till_may_sell_and_an_unknown_token_may_not()
    {
        InstallKey(2);
        var sut = Sut();
        var token = await AddTill(sut);

        Assert.Equal(TillAccess.Allowed, await sut.CheckAccessAsync(token));
        Assert.Equal(TillAccess.NotRegistered, await sut.CheckAccessAsync("nxt_not-a-real-token"));
        Assert.Equal(TillAccess.NotRegistered, await sut.CheckAccessAsync(null));
        Assert.Equal(TillAccess.NotRegistered, await sut.CheckAccessAsync("  "));
    }

    [Fact]
    public async Task A_code_works_once()
    {
        InstallKey(3);
        var sut = Sut();
        var issued = await sut.IssuePairingCodeAsync("Till 2");

        Assert.Equal(PairOutcome.Success, (await sut.PairAsync(issued.Code, "10.0.0.5")).Outcome);
        Assert.Equal(PairOutcome.InvalidCode, (await sut.PairAsync(issued.Code, "10.0.0.6")).Outcome);
    }

    [Fact]
    public async Task A_code_is_accepted_however_it_is_typed()
    {
        InstallKey(2);
        var sut = Sut();
        var issued = await sut.IssuePairingCodeAsync("Till 2");

        var typed = " " + issued.Code!.ToLowerInvariant().Replace("-", " - ") + " ";

        Assert.Equal(PairOutcome.Success, (await sut.PairAsync(typed, "10.0.0.5")).Outcome);
    }

    [Fact]
    public async Task A_code_runs_out_and_then_its_seat_is_free_again()
    {
        InstallKey(2); // one till seat
        var sut = Sut();
        var first = await sut.IssuePairingCodeAsync("Till 2");
        Assert.Equal(IssuePairingOutcome.SeatsFull, (await sut.IssuePairingCodeAsync("Till 3")).Outcome); // the open code holds the seat

        _clock.Advance(TillDeviceService.CodeLifetime + TimeSpan.FromMinutes(1));

        Assert.Equal(PairOutcome.InvalidCode, (await sut.PairAsync(first.Code, "10.0.0.5")).Outcome);
        Assert.Equal(IssuePairingOutcome.Success, (await sut.IssuePairingCodeAsync("Till 3")).Outcome);
        Assert.Single((await sut.GetSeatsAsync()).Devices); // the expired row was cleared away
    }

    [Fact]
    public async Task Guessing_codes_is_throttled_even_when_the_right_one_comes_next()
    {
        InstallKey(3);
        var sut = Sut();
        var issued = await sut.IssuePairingCodeAsync("Till 2");

        for (var i = 0; i < PairingThrottle.MaxFailures; i++)
            Assert.Equal(PairOutcome.InvalidCode, (await sut.PairAsync("AAAA-AAAA", "10.0.0.9")).Outcome);

        Assert.Equal(PairOutcome.TooManyAttempts, (await sut.PairAsync(issued.Code, "10.0.0.9")).Outcome);
        // Someone else on the network is not affected.
        Assert.Equal(PairOutcome.Success, (await sut.PairAsync(issued.Code, "10.0.0.7")).Outcome);
    }

    [Fact]
    public async Task The_throttle_lifts_after_its_window()
    {
        InstallKey(3);
        var sut = Sut();
        for (var i = 0; i < PairingThrottle.MaxFailures; i++) await sut.PairAsync("AAAA-AAAA", "10.0.0.9");

        _clock.Advance(PairingThrottle.Window + TimeSpan.FromSeconds(1));
        var issued = await sut.IssuePairingCodeAsync("Till 2"); // issued after the wait: the window is as long as a code lives

        Assert.Equal(PairOutcome.Success, (await sut.PairAsync(issued.Code, "10.0.0.9")).Outcome);
    }

    [Fact]
    public async Task Neither_the_code_nor_the_token_is_stored_in_the_clear()
    {
        InstallKey(2);
        var sut = Sut();
        var issued = await sut.IssuePairingCodeAsync("Till 2");
        var paired = await sut.PairAsync(issued.Code, "10.0.0.5");

        var row = await _dbs.CreateContext().TillDevices.AsNoTracking().SingleAsync();

        Assert.DoesNotContain(paired.Token!, row.TokenHash);
        Assert.NotEqual(paired.Token, row.TokenHash);
        Assert.Equal(64, row.TokenHash!.Length);
        Assert.Null(row.PairingCodeHash); // used codes are wiped
        Assert.NotNull(row.PairedAt);
    }

    // ---- removing and shrinking --------------------------------------------------------------

    [Fact]
    public async Task Removing_a_till_frees_its_seat_and_cuts_it_off()
    {
        InstallKey(2);
        var sut = Sut();
        var token = await AddTill(sut);
        var id = (await sut.GetSeatsAsync()).Devices.Single().Id;
        Assert.Equal(IssuePairingOutcome.SeatsFull, (await sut.IssuePairingCodeAsync("Another")).Outcome);

        Assert.True(await sut.RevokeAsync(id));

        Assert.Equal(TillAccess.NotRegistered, await sut.CheckAccessAsync(token));
        Assert.Equal(IssuePairingOutcome.Success, (await sut.IssuePairingCodeAsync("Another")).Outcome);
        Assert.False(await sut.RevokeAsync(id)); // already gone
        Assert.False(await sut.RevokeAsync(9999));
    }

    [Fact]
    public async Task Cancelling_an_unused_code_frees_its_seat()
    {
        InstallKey(2);
        var sut = Sut();
        var issued = await sut.IssuePairingCodeAsync("Till 2");

        Assert.True(await sut.RevokeAsync(issued.DeviceId!.Value));

        Assert.Equal(PairOutcome.InvalidCode, (await sut.PairAsync(issued.Code, "10.0.0.5")).Outcome);
        Assert.Equal(IssuePairingOutcome.Success, (await sut.IssuePairingCodeAsync("Till 3")).Outcome);
    }

    [Fact]
    public async Task When_a_smaller_package_is_installed_the_oldest_tills_keep_selling()
    {
        InstallKey(3);
        var sut = Sut();
        var first = await AddTill(sut, "Till 2");
        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = await AddTill(sut, "Till 3");

        InstallKey(2); // one till seat now (a newer-issued key would be needed in real life; the service just reads storage)
        var smaller = Sut();

        Assert.Equal(TillAccess.Allowed, await smaller.CheckAccessAsync(first));
        Assert.Equal(TillAccess.SeatsExceeded, await smaller.CheckAccessAsync(second));
        var devices = (await smaller.GetSeatsAsync()).Devices;
        Assert.False(devices.Single(d => d.Name == "Till 2").Overflow);
        Assert.True(devices.Single(d => d.Name == "Till 3").Overflow);
    }

    [Fact]
    public async Task A_code_issued_before_the_licence_shrank_cannot_pair_past_the_new_limit()
    {
        InstallKey(3);
        var sut = Sut();
        var a = await sut.IssuePairingCodeAsync("Till 2");
        var b = await sut.IssuePairingCodeAsync("Till 3");

        InstallKey(2);
        var smaller = Sut();

        Assert.Equal(PairOutcome.Success, (await smaller.PairAsync(a.Code, "10.0.0.5")).Outcome);
        Assert.Equal(PairOutcome.SeatsFull, (await smaller.PairAsync(b.Code, "10.0.0.6")).Outcome);
    }

    // ---- bookkeeping -------------------------------------------------------------------------

    [Fact]
    public async Task Last_seen_is_written_at_most_every_few_minutes()
    {
        InstallKey(2);
        var sut = Sut();
        var token = await AddTill(sut);
        var pairedAt = (await sut.GetSeatsAsync()).Devices.Single().LastSeenAt;

        _clock.Advance(TimeSpan.FromMinutes(1));
        await sut.CheckAccessAsync(token);
        Assert.Equal(pairedAt, (await sut.GetSeatsAsync()).Devices.Single().LastSeenAt);

        _clock.Advance(TimeSpan.FromMinutes(10));
        await sut.CheckAccessAsync(token);
        Assert.True((await sut.GetSeatsAsync()).Devices.Single().LastSeenAt > pairedAt);
    }

    [Fact]
    public void The_device_registry_is_never_synced_to_the_cloud()
    {
        Assert.Contains("TillDevices", Sync.SyncSchema.ExcludedTables);
    }
}
