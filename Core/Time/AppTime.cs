namespace Core.Time;

/// <summary>
/// Every timestamp the app stores (orders, receipts, sessions, backups, ...) is UTC. People read and
/// pick LOCAL times, so conversion happens only at the edges: <see cref="UtcToLocal(DateTime)"/> when
/// showing a stored value, and <see cref="LocalToUtc(DateTime)"/> / <see cref="LocalDayRangeToUtc"/>
/// when a local date or time-of-day is turned into something to compare with stored values.
///
/// Kind rules (the database hands values back as <see cref="DateTimeKind.Unspecified"/>):
///   UtcToLocal: Unspecified and Utc are UTC; Local is already local and is returned unchanged.
///   LocalToUtc: Unspecified and Local are local; Utc is already UTC and is returned unchanged.
/// The <paramref name="zone"/> parameters exist so tests don't depend on the machine's time zone.
/// </summary>
public static class AppTime
{
    public static DateTime UtcToLocal(this DateTime value, TimeZoneInfo? zone = null) =>
        value.Kind == DateTimeKind.Local
            ? value
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), zone ?? TimeZoneInfo.Local);

    public static DateTime? UtcToLocal(this DateTime? value, TimeZoneInfo? zone = null) =>
        value?.UtcToLocal(zone);

    public static DateTime LocalToUtc(this DateTime value, TimeZoneInfo? zone = null)
    {
        if (value.Kind == DateTimeKind.Utc) return value;

        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        var tz = zone ?? TimeZoneInfo.Local;
        // A wall-clock time skipped by a spring-forward change doesn't exist; move it to the first moment that does.
        if (tz.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    public static DateTime? LocalToUtc(this DateTime? value, TimeZoneInfo? zone = null) =>
        value?.LocalToUtc(zone);

    /// <summary>
    /// Turns a picked local date range into UTC bounds for comparing with stored timestamps: from the start of
    /// the first local day up to (and including) the last tick of the last local day. Any time-of-day on the
    /// inputs is ignored, because the pickers choose whole days.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) LocalDayRangeToUtc(DateTime fromDate, DateTime toDate, TimeZoneInfo? zone = null)
    {
        var from = fromDate.Date.LocalToUtc(zone);
        var to = toDate.Date.AddDays(1).LocalToUtc(zone).AddTicks(-1);
        return (from, to);
    }

    /// <summary>Start of the local day containing <paramref name="localMoment"/>, as UTC.</summary>
    public static DateTime LocalDayStartUtc(DateTime localMoment, TimeZoneInfo? zone = null) =>
        localMoment.Date.LocalToUtc(zone);
}
