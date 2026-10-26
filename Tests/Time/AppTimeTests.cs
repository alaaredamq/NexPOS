using Core.Time;
using Xunit;

namespace Tests.Time;

public class AppTimeTests
{
    // A fixed zone so the tests pass on any machine: Beirut is UTC+2, UTC+3 in summer (DST).
    private static readonly TimeZoneInfo Beirut = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Middle East Standard Time" : "Asia/Beirut");

    [Fact]
    public void UtcToLocal_TreatsDatabaseValues_AsUtc()
    {
        // What EF returns for a stored UTC value: Kind = Unspecified.
        var stored = new DateTime(2026, 10, 26, 9, 39, 0, DateTimeKind.Unspecified);

        var local = stored.UtcToLocal(Beirut);

        Assert.Equal(new DateTime(2026, 10, 26, 11, 39, 0), local); // 26 Oct 2026 is winter time (UTC+2)
    }

    [Fact]
    public void UtcToLocal_UsesTheDatesOwnOffset_SoSummerTimeIsRight()
    {
        var summer = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 7, 1, 12, 0, 0), summer.UtcToLocal(Beirut)); // UTC+3
    }

    [Fact]
    public void UtcToLocal_LeavesValuesThatAreAlreadyLocalAlone()
    {
        var alreadyLocal = new DateTime(2026, 10, 26, 11, 0, 0, DateTimeKind.Local);

        Assert.Equal(alreadyLocal, alreadyLocal.UtcToLocal(Beirut));
    }

    [Fact]
    public void UtcToLocal_Nullable_PassesNullThrough()
    {
        DateTime? none = null;

        Assert.Null(none.UtcToLocal(Beirut));
        Assert.Equal(new DateTime(2026, 10, 26, 11, 0, 0), ((DateTime?)new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc)).UtcToLocal(Beirut));
    }

    [Fact]
    public void LocalToUtc_ConvertsPickedLocalTimes()
    {
        Assert.Equal(new DateTime(2026, 10, 25, 22, 0, 0), new DateTime(2026, 10, 26, 0, 0, 0).LocalToUtc(Beirut)); // local midnight = 22:00Z the day before
        Assert.Equal(new DateTime(2026, 7, 1, 21, 0, 0), new DateTime(2026, 7, 2, 0, 0, 0).LocalToUtc(Beirut));    // summer: UTC+3
    }

    [Fact]
    public void LocalToUtc_LeavesUtcAlone_AndRoundTrips()
    {
        var utc = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, utc.LocalToUtc(Beirut));

        var local = utc.UtcToLocal(Beirut);
        Assert.Equal(utc.Ticks, local.LocalToUtc(Beirut).Ticks);
    }

    [Fact]
    public void LocalDayRangeToUtc_CoversWholeLocalDays_IncludingTheLastTick()
    {
        // The 26th local, 00:00-03:00, is still the 25th in UTC: the case that lost receipts at 00:27.
        var (from, to) = AppTime.LocalDayRangeToUtc(new DateTime(2026, 10, 26), new DateTime(2026, 10, 26), Beirut);

        Assert.Equal(new DateTime(2026, 10, 25, 22, 0, 0), from);
        Assert.Equal(new DateTime(2026, 10, 26, 22, 0, 0).AddTicks(-1), to);

        var receiptAt0027Local = new DateTime(2026, 10, 25, 22, 27, 0, DateTimeKind.Utc);
        Assert.InRange(receiptAt0027Local, from, to);
    }

    [Fact]
    public void LocalDayRangeToUtc_IgnoresTimeOfDayOnTheInputs()
    {
        var (from, to) = AppTime.LocalDayRangeToUtc(new DateTime(2026, 10, 26, 15, 30, 0), new DateTime(2026, 10, 27, 8, 0, 0), Beirut);

        Assert.Equal(new DateTime(2026, 10, 25, 22, 0, 0), from);
        Assert.Equal(new DateTime(2026, 10, 27, 22, 0, 0).AddTicks(-1), to);
    }

    [Fact]
    public void LocalToUtc_ASkippedSpringForwardTime_DoesNotThrow()
    {
        // Lebanon springs forward at 00:00 on the last Sunday of March: 00:30 that day doesn't exist.
        var skipped = new DateTime(2026, 3, 29, 0, 30, 0);
        Assert.True(Beirut.IsInvalidTime(skipped));

        var utc = skipped.LocalToUtc(Beirut);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }
}
