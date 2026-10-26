using System.Text.Json;
using Core.Time;
using Xunit;

namespace Tests.Time;

/// <summary>
/// The browser reads "2026-10-26T09:39:00" (no zone) as LOCAL time. Stored timestamps are UTC, so they must
/// be written with a "Z". Local dates/times are marked Kind=Local where they are produced, and keep their offset.
/// </summary>
public class UtcDateTimeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new UtcDateTimeJsonConverter(), new UtcNullableDateTimeJsonConverter() }
    };

    private record Row(DateTime At, DateTime? MaybeAt);

    private static string Json(DateTime at, DateTime? maybe = null) => JsonSerializer.Serialize(new Row(at, maybe), Options);

    [Fact]
    public void StoredTimestamp_Unspecified_IsWrittenAsUtc()
    {
        var json = Json(new DateTime(2026, 10, 26, 9, 39, 14, DateTimeKind.Unspecified));

        Assert.Contains("\"At\":\"2026-10-26T09:39:14Z\"", json);
    }

    [Fact]
    public void NullableTimestamp_Unspecified_IsWrittenAsUtc_AndNullStaysNull()
    {
        var withValue = Json(DateTime.UtcNow, new DateTime(2026, 10, 26, 9, 39, 14, DateTimeKind.Unspecified));
        var withoutValue = Json(DateTime.UtcNow, null);

        Assert.Contains("\"MaybeAt\":\"2026-10-26T09:39:14Z\"", withValue);
        Assert.Contains("\"MaybeAt\":null", withoutValue);
    }

    [Fact]
    public void ATimestampAtExactlyMidnightUtc_StillGetsItsZone()
    {
        // A backup scheduled for 02:00 in UTC+2 is stored as 00:00:00Z. Treating "midnight" as a date would
        // leave it without a zone and the browser would show it two hours early.
        var json = Json(new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Unspecified));

        Assert.Contains("\"At\":\"2026-10-27T00:00:00Z\"", json);
    }

    [Fact]
    public void LocalDates_MarkedLocal_KeepTheirOffset_SoTheBrowserShowsTheSameDay()
    {
        var pickedDay = DateTime.SpecifyKind(new DateTime(2026, 10, 26, 23, 59, 59), DateTimeKind.Local);

        var json = Json(pickedDay);

        Assert.DoesNotContain("23:59:59Z", json);
        var back = JsonSerializer.Deserialize<Row>(json, Options)!;
        Assert.Equal(26, back.At.ToLocalTime().Day);
    }

    [Fact]
    public void UtcValues_AreWrittenAsBefore()
    {
        var json = Json(new DateTime(2026, 10, 26, 9, 39, 14, DateTimeKind.Utc));

        Assert.Contains("\"At\":\"2026-10-26T09:39:14Z\"", json);
    }

    [Fact]
    public void LocalValues_KeepTheirOffset_NotRelabelledAsUtc()
    {
        var local = new DateTime(2026, 10, 26, 11, 39, 14, DateTimeKind.Local);

        var json = Json(local);

        Assert.DoesNotContain("\"At\":\"2026-10-26T11:39:14Z\"", json);
        // The same instant, read back, must be the same moment the offset says it is.
        var back = JsonSerializer.Deserialize<Row>(json, Options)!;
        Assert.Equal(local.ToUniversalTime(), back.At.ToUniversalTime());
    }

    [Fact]
    public void Reading_RoundTripsInstants()
    {
        var utc = new DateTime(2026, 10, 26, 9, 39, 14, DateTimeKind.Utc);

        var back = JsonSerializer.Deserialize<Row>(Json(utc), Options)!;

        Assert.Equal(utc, back.At.ToUniversalTime());
    }
}
