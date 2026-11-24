using System.Globalization;
using System.Text.RegularExpressions;

namespace Cloud.Data;

/// <summary>
/// The colour sets a public menu can wear. Each is a set of CSS variables in menu.css (<c>html[data-theme=...]</c>), light and dark,
/// chosen to sit well with food photos: one accent for what the guest can do, and a paper tone that suits it.
/// </summary>
public static class MenuThemes
{
    public const string Basil = "basil";       // herb green on porcelain: fresh, any kitchen
    public const string Ember = "ember";       // grill red-orange on warm sand: burgers, grills, pizza
    public const string Saffron = "saffron";   // deep gold on cream: bakeries, breakfast, sweets
    public const string Harbor = "harbor";     // sea blue on cool white: seafood, cafés
    public const string Plum = "plum";         // wine plum on blush: bars, desserts, evening places
    public const string Charcoal = "charcoal"; // ink on stone, no colour: fine dining, coffee roasters

    public const string Default = Basil;
    public static readonly string[] All = [Basil, Ember, Saffron, Harbor, Plum, Charcoal];
    public static bool IsValid(string? theme) => theme is not null && All.Contains(theme);
}

/// <summary>
/// Opening hours, one entry per weekday (Sunday first, as <see cref="DayOfWeek"/>), separated by <c>;</c>. An entry is one or more ranges
/// <c>HH:mm-HH:mm</c> separated by <c>,</c> (a lunch break), or empty for closed. A range that ends at or before it starts runs past
/// midnight (<c>18:00-02:00</c>). Example: <c>;09:00-23:00;09:00-23:00;09:00-23:00;09:00-23:00;12:00-15:00,18:00-01:00;</c>.
/// </summary>
public static partial class MenuHours
{
    public readonly record struct Range(TimeOnly Open, TimeOnly Close)
    {
        public bool Overnight => Close <= Open;
    }

    /// <summary>Where the shop stands now: open (until when), or closed (and when it opens next, within the coming week).</summary>
    public sealed record Status(bool Open, TimeOnly? Until, TimeOnly? Opens, DayOfWeek? OpensOn);

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d-([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex RangeShape();

    /// <summary>The seven days' ranges; days not given are closed. Null or unreadable hours give null.</summary>
    public static List<Range>[]? Parse(string? hours)
    {
        if (string.IsNullOrWhiteSpace(hours)) return null;
        var days = hours.Split(';');
        if (days.Length != 7) return null;
        var week = new List<Range>[7];
        for (var d = 0; d < 7; d++)
        {
            week[d] = [];
            foreach (var part in days[d].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!RangeShape().IsMatch(part)) return null;
                week[d].Add(new Range(TimeOnly.ParseExact(part[..5], "HH:mm", CultureInfo.InvariantCulture), TimeOnly.ParseExact(part[6..], "HH:mm", CultureInfo.InvariantCulture)));
            }
            week[d].Sort((a, b) => a.Open.CompareTo(b.Open));
        }
        return week;
    }

    /// <summary>
    /// Checks and tidies what the editor sends: seven entries (Sunday first), each "" or ranges like "09:00-23:00" or "12:00-15:00,18:00-23:00".
    /// All closed is the same as no hours (null).
    /// </summary>
    public static (string? Value, string? Error) Normalize(IReadOnlyList<string?>? days)
    {
        if (days is null || days.All(string.IsNullOrWhiteSpace)) return (null, null);
        if (days.Count != 7) return (null, "Give the hours for all seven days.");
        var tidy = string.Join(';', days.Select(d => string.Join(',', (d ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))));
        var week = Parse(tidy);
        if (week is null) return (null, "Write each time as 09:00, and each range as 09:00-23:00.");
        if (week.Any(day => day.Count > MaxRanges)) return (null, $"A day can have at most {MaxRanges} opening times.");
        if (week.Any(Overlaps)) return (null, "Two opening times on the same day overlap.");
        // Stored in time order, whatever order they were typed in.
        return (string.Join(';', week.Select(day => string.Join(',', day.Select(r => $"{r.Open:HH\\:mm}-{r.Close:HH\\:mm}")))), null);
    }

    /// <summary>How many separate opening times one day can have (09:00-12:00, 13:00-16:00, 18:00-21:00 is three).</summary>
    public const int MaxRanges = 4;

    /// <summary>Ranges sorted by opening time overlap when one opens before the previous closes; a range past midnight must be the day's last.</summary>
    private static bool Overlaps(List<Range> day)
    {
        for (var i = 1; i < day.Count; i++)
            if (day[i - 1].Overnight || day[i].Open < day[i - 1].Close) return true;
        return false;
    }

    /// <summary>Open or closed at <paramref name="local"/> (the shop's local time), and the next change.</summary>
    public static Status? At(string? hours, DateTime local)
    {
        var week = Parse(hours);
        if (week is null) return null;
        var now = TimeOnly.FromDateTime(local);
        var today = (int)local.DayOfWeek;
        var yesterday = (today + 6) % 7;

        // Still inside last night's late range (Friday 18:00-02:00, now Saturday 01:00)?
        foreach (var r in week[yesterday].Where(r => r.Overnight))
            if (now < r.Close) return new Status(true, r.Close, null, null);
        foreach (var r in week[today])
            if (now >= r.Open && (r.Overnight || now < r.Close)) return new Status(true, r.Close, null, null);

        // Closed: the next opening today, else on a following day.
        if (week[today].Where(r => r.Open > now).Select(r => (Range?)r).FirstOrDefault() is { } later)
            return new Status(false, null, later.Open, local.DayOfWeek);
        for (var ahead = 1; ahead <= 7; ahead++)
        {
            var day = (today + ahead) % 7;
            if (week[day].Count > 0) return new Status(false, null, week[day][0].Open, (DayOfWeek)day);
        }
        return new Status(false, null, null, null);
    }
}

/// <summary>Turns what the owner typed (a handle, a number, a bare address) into safe links, and checks it on the way in.</summary>
public static partial class MenuContact
{
    [GeneratedRegex(@"^@?[A-Za-z0-9._\-]{1,60}$")]
    private static partial Regex Handle();

    [GeneratedRegex(@"^\+?[0-9 ()\-]{6,24}$")]
    private static partial Regex PhoneShape();

    private static string? Http(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.ToString() : null;

    private static string Digits(string s) => new(s.Where(char.IsAsciiDigit).ToArray());

    public static string? PhoneLink(string? phone) => string.IsNullOrWhiteSpace(phone) ? null : "tel:" + (phone.TrimStart().StartsWith('+') ? "+" : "") + Digits(phone);

    /// <summary>A wa.me link, with a message filled in when given.</summary>
    public static string? WhatsAppLink(string? number, string? text = null)
        => string.IsNullOrWhiteSpace(number) ? null : "https://wa.me/" + Digits(number) + (text is null ? "" : "?text=" + Uri.EscapeDataString(text));

    public static string? InstagramLink(string? v) => Social(v, "https://instagram.com/");
    public static string? FacebookLink(string? v) => Social(v, "https://facebook.com/");
    public static string? TikTokLink(string? v) => Social(v, "https://www.tiktok.com/@");
    public static string? WebsiteLink(string? v) => string.IsNullOrWhiteSpace(v) ? null : Http(v.Contains("://") ? v : "https://" + v);

    /// <summary>The map link given, else a map search for the address.</summary>
    public static string? MapLink(string? mapUrl, string? address)
        => Http(mapUrl) ?? (string.IsNullOrWhiteSpace(address) ? null : "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(address));

    private static string? Social(string? v, string root)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (v.Contains("://")) return Http(v);
        return Handle().IsMatch(v) ? root + v.TrimStart('@') : null;
    }

    /// <summary>What a handle field accepts: @name, name, or a full link.</summary>
    public static bool IsHandle(string v) => Handle().IsMatch(v) || Http(v) is not null;
    public static bool IsPhone(string v) => PhoneShape().IsMatch(v);
    public static bool IsLink(string v) => Http(v) is not null;
    public static bool IsSite(string v) => WebsiteLink(v) is not null && v.Contains('.');
}
