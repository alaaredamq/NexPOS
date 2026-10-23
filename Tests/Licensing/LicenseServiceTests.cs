using Application.DTOs.Licensing;
using Application.Exceptions;
using Common.Licensing;
using Xunit;

namespace Tests.Licensing;

public class LicenseServiceTests : IDisposable
{
    private readonly LicenseTestIssuer _issuer = new();
    private readonly InMemoryLicenseStorage _storage = new();

    // Provisional license that expires on 2026-11-01 (the timeline below is relative to this).
    private static readonly DateOnly Expiry = new(2026, 11, 1);
    private static readonly DateOnly IssuedOn = new(2026, 10, 1);

    public void Dispose() => _issuer.Dispose();

    private Application.Services.Licensing.LicenseService Service(DateOnly today, DateOnly? buildDate = null, bool enforce = true, MachineId? machine = null) =>
        LicenseServiceFactory.Create(_issuer, _storage, new FakeClock(today), machine, buildDate, enforce);

    private void Store(LicenseKind kind, DateOnly validUntil, DateOnly? issuedOn = null, MachineId? machine = null) =>
        _storage.Text = _issuer.Issue(kind, machine ?? TestMachines.ShopPc, issuedOn ?? IssuedOn, validUntil);

    // ---- nothing / bad / wrong PC -----------------------------------------------------------

    [Fact]
    public void No_license_means_not_activated_and_no_sales()
    {
        var state = Service(Expiry).GetState();

        Assert.Equal(LicenseStatus.NotActivated, state.Status);
        Assert.False(state.AllowsSales);
        Assert.Equal(TestMachines.ShopPc.Format(), state.MachineId);
    }

    [Fact]
    public void A_corrupted_license_file_is_invalid()
    {
        _storage.Text = "NXL1-AAAAA-BBBBB-CCCCC";

        var state = Service(Expiry).GetState();

        Assert.Equal(LicenseStatus.Invalid, state.Status);
        Assert.False(state.AllowsSales);
    }

    [Fact]
    public void A_license_signed_by_another_issuer_is_invalid()
    {
        using var forger = new LicenseTestIssuer();
        _storage.Text = forger.Issue(LicenseKind.Full, TestMachines.ShopPc, IssuedOn, Expiry.AddYears(1));

        Assert.Equal(LicenseStatus.Invalid, Service(Expiry).GetState().Status);
    }

    [Fact]
    public void A_license_copied_to_another_pc_is_wrong_machine()
    {
        Store(LicenseKind.Full, Expiry.AddYears(1), machine: TestMachines.OtherPc);

        var state = Service(Expiry).GetState();

        Assert.Equal(LicenseStatus.WrongMachine, state.Status);
        Assert.False(state.AllowsSales);
    }

    // ---- Full (perpetual) -------------------------------------------------------------------

    [Fact]
    public void A_full_license_never_expires()
    {
        Store(LicenseKind.Full, new DateOnly(2027, 10, 1));

        // Five years after the updates plan ended, with a build from before it ended.
        var state = Service(new DateOnly(2032, 1, 1), buildDate: new DateOnly(2026, 10, 1)).GetState();

        Assert.Equal(LicenseStatus.Active, state.Status);
        Assert.True(state.AllowsSales);
        Assert.False(state.ShowsWarning);
    }

    [Fact]
    public void A_full_license_runs_a_build_released_on_the_last_day_of_the_updates_plan()
    {
        Store(LicenseKind.Full, new DateOnly(2027, 10, 1));

        Assert.Equal(LicenseStatus.Active, Service(Expiry, buildDate: new DateOnly(2027, 10, 1)).GetState().Status);
    }

    [Fact]
    public void A_full_license_refuses_a_build_released_after_the_updates_plan_ended()
    {
        Store(LicenseKind.Full, new DateOnly(2027, 10, 1));

        var state = Service(Expiry, buildDate: new DateOnly(2027, 10, 2)).GetState();

        Assert.Equal(LicenseStatus.UpdatesNotCovered, state.Status);
        Assert.False(state.AllowsSales);
    }

    // ---- time-limited timeline: warn a week before, a week after, then stop ------------------

    [Theory]
    [InlineData("2026-10-01", LicenseStatus.Active, true)]            // a month before
    [InlineData("2026-10-24", LicenseStatus.Active, true)]            // 8 days left: still quiet
    [InlineData("2026-10-25", LicenseStatus.ExpiringSoon, true)]      // 7 days left: warning starts
    [InlineData("2026-10-31", LicenseStatus.ExpiringSoon, true)]      // 1 day left
    [InlineData("2026-11-01", LicenseStatus.ExpiringSoon, true)]      // last valid day
    [InlineData("2026-11-02", LicenseStatus.GracePeriod, true)]       // expired yesterday
    [InlineData("2026-11-08", LicenseStatus.GracePeriod, true)]       // 7th day after expiry: last day of selling
    [InlineData("2026-11-09", LicenseStatus.Expired, false)]          // POS and new sales stop
    [InlineData("2027-03-01", LicenseStatus.Expired, false)]          // months later
    public void Provisional_license_timeline(string today, LicenseStatus expected, bool allowsSales)
    {
        Store(LicenseKind.Provisional, Expiry);

        var state = Service(DateOnly.Parse(today)).GetState();

        Assert.Equal(expected, state.Status);
        Assert.Equal(allowsSales, state.AllowsSales);
    }

    [Fact]
    public void Trial_license_follows_the_same_timeline()
    {
        Store(LicenseKind.Trial, Expiry);

        Assert.Equal(LicenseStatus.ExpiringSoon, Service(new DateOnly(2026, 10, 28)).GetState().Status);
        Assert.Equal(LicenseStatus.GracePeriod, Service(new DateOnly(2026, 11, 3)).GetState().Status);
        Assert.Equal(LicenseStatus.Expired, Service(new DateOnly(2026, 11, 20)).GetState().Status);
    }

    [Theory]
    [InlineData("2026-10-25", 7)]
    [InlineData("2026-10-30", 2)]
    [InlineData("2026-11-01", 0)]
    public void Warning_counts_down_the_days_left(string today, int daysLeft)
    {
        Store(LicenseKind.Provisional, Expiry);

        var state = Service(DateOnly.Parse(today)).GetState();

        Assert.Equal(daysLeft, state.DaysLeft);
        Assert.True(state.ShowsWarning);
        Assert.Equal(Expiry, state.ValidUntil);
        Assert.Equal(Expiry.AddDays(7), state.SalesStopAfter);
    }

    [Theory]
    [InlineData("2026-11-02", 6)]
    [InlineData("2026-11-05", 3)]
    [InlineData("2026-11-08", 0)]
    public void Grace_period_counts_down_the_days_until_selling_stops(string today, int daysUntilStop)
    {
        Store(LicenseKind.Provisional, Expiry);

        var state = Service(DateOnly.Parse(today)).GetState();

        Assert.Equal(daysUntilStop, state.DaysUntilStop);
        Assert.True(state.ShowsWarning);
        Assert.Equal(new DateOnly(2026, 11, 8), state.SalesStopAfter);
    }

    [Fact]
    public void An_expired_license_still_reports_who_it_was_for_so_the_screen_can_say_so()
    {
        Store(LicenseKind.Provisional, Expiry);

        var state = Service(new DateOnly(2026, 12, 25)).GetState();

        Assert.Equal(LicenseStatus.Expired, state.Status);
        Assert.Equal("Test Shop", state.Customer);
        Assert.Equal(Expiry, state.ValidUntil);
    }

    // ---- the sales gate ---------------------------------------------------------------------

    [Fact]
    public void EnsureSalesAllowed_passes_while_selling_is_allowed()
    {
        Store(LicenseKind.Provisional, Expiry);

        Service(new DateOnly(2026, 10, 28)).EnsureSalesAllowed();   // warning week
        Service(new DateOnly(2026, 11, 8)).EnsureSalesAllowed();    // last grace day
    }

    [Theory]
    [InlineData("2026-11-09")]
    [InlineData("2027-01-01")]
    public void EnsureSalesAllowed_throws_once_the_grace_week_is_over(string today)
    {
        Store(LicenseKind.Provisional, Expiry);

        var ex = Assert.Throws<LicenseRequiredException>(() => Service(DateOnly.Parse(today)).EnsureSalesAllowed());
        Assert.Equal(LicenseStatus.Expired, ex.Status);
    }

    [Fact]
    public void EnsureSalesAllowed_throws_when_nothing_is_activated()
    {
        var ex = Assert.Throws<LicenseRequiredException>(() => Service(Expiry).EnsureSalesAllowed());
        Assert.Equal(LicenseStatus.NotActivated, ex.Status);
    }

    [Fact]
    public void Debug_builds_do_not_enforce_anything()
    {
        var state = Service(Expiry, enforce: false).GetState();

        Assert.Equal(LicenseStatus.Active, state.Status);
        Assert.True(state.EnforcementDisabled);
        Assert.True(state.AllowsSales);
    }

    [Fact]
    public void A_release_build_without_a_public_key_refuses_to_start()
    {
        var options = new Application.Services.Licensing.LicenseOptions { PublicKeySpkiBase64 = "", BuildDate = Expiry };

        Assert.Throws<InvalidOperationException>(() => new Application.Services.Licensing.LicenseService(
            options, _storage, new FakeMachineIdProvider(TestMachines.ShopPc), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Services.Licensing.LicenseService>.Instance));
    }

    // ---- activation -------------------------------------------------------------------------

    [Fact]
    public void Activating_a_valid_key_unlocks_the_pos_and_survives_a_restart()
    {
        var key = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, IssuedOn, new DateOnly(2027, 10, 1));
        var service = Service(Expiry);
        Assert.Equal(LicenseStatus.NotActivated, service.GetState().Status);

        var result = service.Activate(key);

        Assert.True(result.Success);
        Assert.Equal(LicenseStatus.Active, result.State.Status);
        Assert.Equal(LicenseStatus.Active, service.GetState().Status);

        // A brand-new service instance (app restart) reads it back from storage.
        Assert.Equal(LicenseStatus.Active, Service(Expiry).GetState().Status);
    }

    [Fact]
    public void Activation_stores_the_canonical_text_whatever_was_pasted()
    {
        var key = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, IssuedOn, new DateOnly(2027, 10, 1));

        Service(Expiry).Activate("  " + key.ToLowerInvariant().Replace("-", " ") + "\r\n");

        Assert.Equal(key, _storage.Text);
    }

    [Fact]
    public void Activating_a_key_for_another_pc_is_refused_and_changes_nothing()
    {
        var foreign = _issuer.Issue(LicenseKind.Full, TestMachines.OtherPc, IssuedOn, new DateOnly(2027, 10, 1));

        var result = Service(Expiry).Activate(foreign);

        Assert.False(result.Success);
        Assert.Equal(LicenseActivationError.WrongMachine, result.Error);
        Assert.Null(_storage.Text);
        Assert.Equal(LicenseStatus.NotActivated, result.State.Status);
    }

    [Fact]
    public void Activating_a_tampered_or_foreign_key_is_refused()
    {
        using var forger = new LicenseTestIssuer();
        var forged = forger.Issue(LicenseKind.Full, TestMachines.ShopPc, IssuedOn, new DateOnly(2027, 10, 1));

        var result = Service(Expiry).Activate(forged);

        Assert.False(result.Success);
        Assert.Equal(LicenseActivationError.BadSignature, result.Error);
        Assert.Null(_storage.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a key")]
    public void Activating_garbage_is_refused(string text)
    {
        var result = Service(Expiry).Activate(text);

        Assert.False(result.Success);
        Assert.Equal(LicenseActivationError.Malformed, result.Error);
    }

    [Fact]
    public void Renewing_an_expired_provisional_license_with_a_full_one_brings_selling_back()
    {
        Store(LicenseKind.Provisional, Expiry);
        var service = Service(new DateOnly(2026, 12, 1));
        Assert.Equal(LicenseStatus.Expired, service.GetState().Status);

        var full = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, new DateOnly(2026, 11, 20), new DateOnly(2027, 11, 20));
        var result = service.Activate(full);

        Assert.True(result.Success);
        Assert.Equal(LicenseStatus.Active, service.GetState().Status);
        service.EnsureSalesAllowed();
    }

    [Fact]
    public void An_older_key_cannot_replace_a_newer_one()
    {
        var newer = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, new DateOnly(2026, 11, 20), new DateOnly(2027, 11, 20));
        var older = _issuer.Issue(LicenseKind.Provisional, TestMachines.ShopPc, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 15));
        var service = Service(new DateOnly(2026, 11, 25));
        Assert.True(service.Activate(newer).Success);

        var result = service.Activate(older);

        Assert.False(result.Success);
        Assert.Equal(LicenseActivationError.OlderThanCurrent, result.Error);
        Assert.Equal(LicenseStatus.Active, service.GetState().Status);
        Assert.Equal(newer, _storage.Text);
    }

    [Fact]
    public void Same_day_full_beats_time_limited_in_either_order()
    {
        var day = new DateOnly(2026, 10, 15);
        var provisional = _issuer.Issue(LicenseKind.Provisional, TestMachines.ShopPc, day, new DateOnly(2026, 11, 29));
        var full = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, day, new DateOnly(2027, 10, 15));

        var service = Service(day);
        Assert.True(service.Activate(provisional).Success);
        Assert.True(service.Activate(full).Success);
        Assert.False(service.Activate(provisional).Success);
    }

    [Fact]
    public void A_stale_wrong_machine_file_does_not_block_activating_a_correct_key()
    {
        Store(LicenseKind.Full, new DateOnly(2027, 10, 1), machine: TestMachines.OtherPc);
        var service = Service(Expiry);
        Assert.Equal(LicenseStatus.WrongMachine, service.GetState().Status);

        var good = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, IssuedOn.AddDays(-30), new DateOnly(2027, 10, 1));

        Assert.True(service.Activate(good).Success);
        Assert.Equal(LicenseStatus.Active, service.GetState().Status);
    }
}
