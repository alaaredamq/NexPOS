using System.Linq.Expressions;
using Application.DTOs.Licensing;
using Application.Exceptions;
using Application.IServices.Licensing;
using Application.Services.Licensing;
using Common.Licensing;
using Core.Entities.Users;
using Core.IRepositories.Users;
using Moq;
using Xunit;

namespace Tests.Licensing;

/// <summary>The Basic / Pro plan: how it is written in the key, what the licence state makes of it, and the user limit.</summary>
public class LicensePlanTests : IDisposable
{
    private static readonly DateOnly Issued = new(2026, 10, 17);
    private static readonly DateOnly Until = new(2027, 10, 17);

    private readonly LicenseTestIssuer _issuer = new();
    private readonly InMemoryLicenseStorage _storage = new();

    public void Dispose() => _issuer.Dispose();

    private LicenseState State(bool enforce = true) =>
        LicenseServiceFactory.Create(_issuer, _storage, new FakeClock(new DateOnly(2026, 11, 15)), enforce: enforce).GetState();

    private static byte FormatVersionOf(string key)
    {
        Assert.True(Base32.TryDecode(string.Concat(key.Split('-').Skip(1)), out var raw));
        return raw[0];
    }

    // ---- the key ------------------------------------------------------------------------------

    [Fact]
    public void A_basic_key_round_trips_with_every_other_field()
    {
        var key = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, "متجر علاء", 0xDEADBEEF, plan: LicensePlan.Basic);

        var payload = LicenseCodec.Decode(key, _issuer.PublicKeySpki).Payload!;

        Assert.Equal(LicensePlan.Basic, payload.Plan);
        Assert.Equal(1, payload.Seats);
        Assert.Equal(LicenseKind.Full, payload.Kind);
        Assert.Equal("متجر علاء", payload.Customer);
        Assert.Equal(0xDEADBEEFu, payload.Serial);
        Assert.Equal(Until, payload.ValidUntil);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    public void A_pro_key_is_written_in_the_old_formats_so_installed_builds_still_accept_it(int seats, int version)
    {
        var key = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, seats: seats, plan: LicensePlan.Pro);

        Assert.Equal(version, FormatVersionOf(key));
        var payload = LicenseCodec.Decode(key, _issuer.PublicKeySpki).Payload!;
        Assert.Equal(LicensePlan.Pro, payload.Plan);
        Assert.Equal(seats, payload.Seats);
    }

    [Fact]
    public void A_basic_key_uses_the_new_format_so_a_build_without_plans_refuses_it_instead_of_running_pro()
    {
        var key = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, plan: LicensePlan.Basic);

        Assert.Equal(3, FormatVersionOf(key));
    }

    [Fact]
    public void Every_key_issued_before_plans_existed_is_pro()
    {
        // The default of the payload is what the old tools wrote: no plan at all.
        var key = LicenseCodec.Encode(new LicensePayload(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, 7, "Old Shop"), _issuer.PrivateKey);

        Assert.Equal(1, FormatVersionOf(key));
        Assert.Equal(LicensePlan.Pro, LicenseCodec.Decode(key, _issuer.PublicKeySpki).Payload!.Plan);
    }

    [Fact]
    public void A_basic_key_cannot_cover_more_than_one_pc()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, seats: 2, plan: LicensePlan.Basic));
    }

    [Fact]
    public void The_plan_cannot_be_changed_by_editing_the_key()
    {
        var key = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, plan: LicensePlan.Basic);

        Assert.True(Base32.TryDecode(string.Concat(key.Split('-').Skip(1)), out var raw));
        raw[raw.Length - 64 - 1] = (byte)LicensePlan.Pro; // the plan byte sits just before the signature
        var result = LicenseCodec.Decode(LicenseCodec.Canonicalize(Base32.Encode(raw)), _issuer.PublicKeySpki);

        Assert.False(result.Success);
        Assert.Equal(LicenseDecodeError.BadSignature, result.Error);
    }

    // ---- the licence state ----------------------------------------------------------------------

    [Fact]
    public void A_basic_licence_locks_the_pro_features_and_caps_users()
    {
        _storage.Text = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, plan: LicensePlan.Basic);

        var state = State();

        Assert.Equal(LicensePlan.Basic, state.Plan);
        Assert.True(state.AllowsSales);
        Assert.Equal(1, state.Seats);
        Assert.Equal(PlanRules.BasicMaxActiveUsers, state.MaxActiveUsers);
        foreach (var feature in Enum.GetValues<ProFeature>()) Assert.False(state.Includes(feature));
    }

    [Fact]
    public void A_pro_licence_includes_everything_and_keeps_its_seats()
    {
        _storage.Text = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, seats: 3, plan: LicensePlan.Pro);

        var state = State();

        Assert.Equal(LicensePlan.Pro, state.Plan);
        Assert.Equal(3, state.Seats);
        Assert.Null(state.MaxActiveUsers);
        foreach (var feature in Enum.GetValues<ProFeature>()) Assert.True(state.Includes(feature));
    }

    [Fact]
    public void No_licence_is_basic()
    {
        Assert.Equal(LicensePlan.Basic, State().Plan);
    }

    [Fact]
    public void A_debug_build_without_a_key_is_pro_but_a_basic_key_still_tries_basic()
    {
        Assert.Equal(LicensePlan.Pro, State(enforce: false).Plan);

        _storage.Text = _issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, plan: LicensePlan.Basic);
        Assert.Equal(LicensePlan.Basic, State(enforce: false).Plan);
    }

    // ---- the user limit ------------------------------------------------------------------------

    private static UserLimitGuard Guard(LicensePlan plan, params bool[] activeFlags)
    {
        var license = new Mock<ILicenseService>();
        license.Setup(l => l.GetState()).Returns(new LicenseState
        {
            Status = LicenseStatus.Active,
            MachineId = "X",
            BuildDate = Issued,
            Plan = plan,
        });

        var users = activeFlags.Select((active, i) =>
            new User($"user{i}", $"u{i}@shop.test", "hash", 1, "U", i.ToString()) { Id = i + 1, IsActive = active }).ToList();
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<User, bool>>?>()))
            .ReturnsAsync((Expression<Func<User, bool>>? filter) => filter is null ? users : users.Where(filter.Compile()).ToList());

        return new UserLimitGuard(license.Object, repo.Object);
    }

    [Fact]
    public async Task Basic_takes_a_third_active_user()
    {
        await Guard(LicensePlan.Basic, true, true).EnsureRoomForActiveUserAsync();
    }

    [Fact]
    public async Task Basic_refuses_a_fourth_active_user_whatever_the_roles()
    {
        var ex = await Assert.ThrowsAsync<PlanLimitException>(() => Guard(LicensePlan.Basic, true, true, true).EnsureRoomForActiveUserAsync());

        Assert.Equal(ProFeature.MoreUsers, ex.Feature);
        Assert.Equal(3, ex.Limit);
    }

    [Fact]
    public async Task Inactive_users_do_not_count()
    {
        await Guard(LicensePlan.Basic, true, true, false, false).EnsureRoomForActiveUserAsync();
    }

    [Fact]
    public async Task Switching_a_user_on_does_not_count_that_user_twice()
    {
        // Three users, the third inactive: switching it on brings the shop to exactly three.
        await Guard(LicensePlan.Basic, true, true, false).EnsureRoomForActiveUserAsync(exceptUserId: 3);
    }

    [Fact]
    public async Task Pro_has_no_user_limit()
    {
        await Guard(LicensePlan.Pro, Enumerable.Repeat(true, 20).ToArray()).EnsureRoomForActiveUserAsync();
    }
}
