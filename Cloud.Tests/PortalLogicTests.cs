using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cloud.Tests;

public class PortalLogicTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static PortalFormat Format(string zone = "UTC")
        => new(Options.Create(new CloudOptions { TimeZone = zone }), new TestClock { Now = new DateTimeOffset(Now) });

    private static Site ConnectedSite(DateTime? seen, string name = "Main till")
    {
        var site = new Site { Id = Guid.NewGuid(), Name = name, LastSeenAt = seen, CreatedAt = Now.AddDays(-30) };
        site.Tokens.Add(new DeviceToken { TokenHash = "h" + Guid.NewGuid(), CreatedAt = Now.AddDays(-30) });
        return site;
    }

    private static Client ClientWith(int expiresInDays, params Site[] sites)
    {
        var c = new Client { Id = 1, Name = "Hassan Minimarket", IsActive = true, CreatedAt = Now.AddDays(-60) };
        c.Subscription = new Subscription { StartsAt = Now.AddDays(-300), ExpiresAt = Now.AddDays(expiresInDays) };
        c.Sites.AddRange(sites);
        return c;
    }

    // ---- what needs the provider ----

    [Fact]
    public void A_healthy_client_needs_nothing()
        => Assert.Empty(AttentionRules.For(ClientWith(200, ConnectedSite(Now.AddMinutes(-3))), Now, Format()));

    [Fact]
    public void A_quiet_shop_is_not_down_while_its_pc_still_checks_in()
    {
        // No data for a week (the shop is closed), but the till pinged 5 minutes ago: perfectly fine.
        var site = ConnectedSite(seen: Now.AddMinutes(-5));
        site.LastSyncAt = Now.AddDays(-7);
        Assert.Empty(AttentionRules.For(ClientWith(200, site), Now, Format()));
    }

    [Fact]
    public void A_connected_till_not_heard_from_for_over_a_day_is_flagged_as_no_contact()
    {
        var items = AttentionRules.For(ClientWith(200, ConnectedSite(Now.AddDays(-2))), Now, Format());

        var item = Assert.Single(items);
        Assert.Equal(AttentionSeverity.Bad, item.Severity);
        Assert.Contains("No contact since 2 days ago", item.Reason);
    }

    [Theory]
    [InlineData(10, AttentionSeverity.Warn, "Online ends in 10 days")]
    [InlineData(-3, AttentionSeverity.Warn, "grace week")]
    [InlineData(-30, AttentionSeverity.Bad, "Online ended on")]
    public void A_subscription_that_is_ending_or_over_asks_for_a_renewal(int expiresInDays, AttentionSeverity severity, string text)
    {
        var item = Assert.Single(AttentionRules.For(ClientWith(expiresInDays, ConnectedSite(Now.AddMinutes(-3))), Now, Format()));

        Assert.Equal(severity, item.Severity);
        Assert.Contains(text, item.Reason);
        Assert.Equal("Renew", item.Action);
    }

    [Fact]
    public void A_link_nobody_used_for_two_days_is_flagged_but_a_fresh_one_is_not()
    {
        Site Waiting(DateTime createdAt)
        {
            var s = new Site { Id = Guid.NewGuid(), Name = "Main till", CreatedAt = createdAt };
            s.EnrollmentCodes.Add(new EnrollmentCode { CodeHash = "x" + Guid.NewGuid(), CreatedAt = createdAt, ExpiresAt = createdAt.AddDays(7) });
            return s;
        }

        Assert.Empty(AttentionRules.For(ClientWith(200, Waiting(Now.AddHours(-5))), Now, Format()));
        var item = Assert.Single(AttentionRules.For(ClientWith(200, Waiting(Now.AddDays(-3))), Now, Format()));
        Assert.Contains("not used yet", item.Reason);
    }

    [Fact]
    public void An_unused_expired_link_and_a_client_with_no_till_are_flagged()
    {
        var s = new Site { Id = Guid.NewGuid(), Name = "Main till", CreatedAt = Now.AddDays(-20) };
        s.EnrollmentCodes.Add(new EnrollmentCode { CodeHash = "y", CreatedAt = Now.AddDays(-20), ExpiresAt = Now.AddDays(-13) });
        Assert.Contains(AttentionRules.For(ClientWith(200, s), Now, Format()), i => i.Reason.Contains("expired unused"));

        Assert.Contains(AttentionRules.For(ClientWith(200), Now, Format()), i => i.Reason == "No branch added yet");
    }

    [Fact]
    public void Clients_switched_off_on_purpose_and_disabled_tills_are_left_alone()
    {
        var off = ClientWith(-90, ConnectedSite(Now.AddDays(-9)));
        off.IsActive = false;
        Assert.Empty(AttentionRules.For(off, Now, Format()));

        var disabled = ConnectedSite(Now.AddDays(-9));
        disabled.IsDisabled = true;
        Assert.Empty(AttentionRules.For(ClientWith(200, disabled), Now, Format()));
    }

    // ---- the 24-hour contact strip ----

    [Fact]
    public void The_strip_is_24_hours_oldest_first_ending_with_this_hour()
    {
        var pulse = PulseData.Build([], Now.AddMinutes(35)); // 12:35

        Assert.Equal(24, pulse.Cells.Count);
        Assert.Equal(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc), pulse.Cells[^1].HourUtc);
        Assert.Equal(new DateTime(2026, 11, 4, 13, 0, 0, DateTimeKind.Utc), pulse.Cells[0].HourUtc);
        Assert.Equal(0, pulse.LitHours);
    }

    [Fact]
    public void Contacts_light_their_hour_and_a_power_cut_shows_as_a_gap()
    {
        var beats = new[]
        {
            new SiteHeartbeat { Hour = new DateTime(2026, 11, 5, 12, 0, 0), Contacts = 3 },
            new SiteHeartbeat { Hour = new DateTime(2026, 11, 5, 11, 0, 0), Contacts = 7 },
            // 10:00 and 09:00 missing: the PC was off
            new SiteHeartbeat { Hour = new DateTime(2026, 11, 5, 8, 0, 0), Contacts = 6 },
            new SiteHeartbeat { Hour = new DateTime(2026, 11, 3, 8, 0, 0), Contacts = 99 }, // outside the window
        };

        var pulse = PulseData.Build(beats, Now);

        Assert.Equal(3, pulse.LitHours);
        Assert.Equal(3, pulse.Cells[^1].Contacts);
        Assert.Equal(7, pulse.Cells[^2].Contacts);
        Assert.Equal(0, pulse.Cells[^3].Contacts);
        Assert.Equal(0, pulse.Cells[^4].Contacts);
        Assert.Equal(6, pulse.Cells[^5].Contacts);
    }

    [Fact]
    public void Several_tills_of_one_client_light_an_hour_when_any_of_them_was_heard()
    {
        var beats = new[]
        {
            new SiteHeartbeat { SiteId = Guid.NewGuid(), Hour = new DateTime(2026, 11, 5, 11, 0, 0), Contacts = 1 },
            new SiteHeartbeat { SiteId = Guid.NewGuid(), Hour = new DateTime(2026, 11, 5, 11, 0, 0), Contacts = 2 },
        };
        Assert.Equal(3, PulseData.Build(beats, Now).Cells[^2].Contacts);
    }

    // ---- reading times ----

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(5, "5 min ago")]
    [InlineData(59, "59 min ago")]
    [InlineData(60, "1 h ago")]
    [InlineData(60 * 23, "23 h ago")]
    [InlineData(60 * 24, "yesterday")]
    [InlineData(60 * 24 * 5, "5 days ago")]
    public void Ago_reads_like_a_person_would_say_it(int minutes, string expected)
        => Assert.Equal(expected, Format().Ago(Now.AddMinutes(-minutes)));

    [Fact]
    public void Ago_says_never_for_nothing()
        => Assert.Equal("never", Format().Ago(null));

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "tomorrow")]
    [InlineData(9, "in 9 days")]
    [InlineData(-1, "yesterday")]
    [InlineData(-4, "4 days ago")]
    public void Until_reads_an_end_date(int days, string expected)
        => Assert.Equal(expected, Format().Until(Now.AddDays(days).AddHours(2)));

    [Fact]
    public void Times_are_shown_in_the_providers_time_zone_while_data_stays_utc()
    {
        var beirut = Format("Asia/Beirut");
        Assert.Equal("2026-11-05", beirut.Day(Now));            // 12:00 UTC is 14:00 in Beirut in November
        Assert.Equal("5 Nov, 14:00", beirut.Clock(Now));
        Assert.Equal("5 Nov, 12:00", Format("UTC").Clock(Now));
        Assert.Equal("5 Nov, 12:00", Format("Not/AZone").Clock(Now)); // an unknown zone falls back to UTC instead of failing
    }

    // ---- wording lives in one place ----

    [Fact]
    public void Every_state_has_a_label_and_a_tone()
    {
        foreach (var state in Enum.GetValues<SubscriptionState>())
        {
            var (tone, label) = Pills.Plan(state);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.Contains(tone, new[] { "ok", "warn", "bad", "none" });
        }
        foreach (var health in Enum.GetValues<SyncHealth>())
            Assert.False(string.IsNullOrWhiteSpace(Pills.Health(health).Label));
        foreach (var c in Enum.GetValues<ConnectionState>())
            Assert.False(string.IsNullOrWhiteSpace(SiteInsight.Label(c) + SiteInsight.Help(c)));
    }
}
