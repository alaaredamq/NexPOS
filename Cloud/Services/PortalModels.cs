using Cloud.Data;
using Microsoft.Extensions.Options;

namespace Cloud.Services;

/// <summary>Times and dates as the provider reads them: in their own time zone, and "3 min ago" rather than a timestamp.</summary>
public sealed class PortalFormat
{
    private readonly TimeProvider _time;

    public PortalFormat(IOptions<CloudOptions> options, TimeProvider time)
    {
        _time = time;
        Zone = Resolve(options.Value.TimeZone);
    }

    public TimeZoneInfo Zone { get; }

    public DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static TimeZoneInfo Resolve(string id)
    {
        // IANA names work on Linux and, with ICU, on Windows; the older Windows id is the fallback for Beirut.
        foreach (var candidate in new[] { id, id == "Asia/Beirut" ? "Middle East Standard Time" : id })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    public DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>"6 Nov, 10:40" in the provider's time zone.</summary>
    public string Clock(DateTime utc) => Local(utc).ToString("d MMM, HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"2026-11-13": dates that matter (expiry) stay unambiguous.</summary>
    public string Day(DateTime utc) => Local(utc).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>How long ago something happened.</summary>
    public string Ago(DateTime? utc, string never = "never")
    {
        if (utc is null) return never;
        var span = Now - utc.Value;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        var days = (int)span.TotalDays;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }

    /// <summary>"in 9 days", "today", "3 days ago": for an end date.</summary>
    public string Until(DateTime utc)
    {
        var days = (int)Math.Floor((utc - Now).TotalDays);
        return days switch { 0 => "today", 1 => "tomorrow", > 1 => $"in {days} days", -1 => "yesterday", _ => $"{-days} days ago" };
    }
}

public enum ConnectionState { NotLinked, WaitingForTill, LinkExpired, Connected, Disconnected }

public static class SiteInsight
{
    /// <summary>Needs the site loaded with its tokens and enrolment codes.</summary>
    public static ConnectionState Connection(Site site, DateTime now)
        => site.Tokens.Any(t => t.RevokedAt == null) ? ConnectionState.Connected
         : site.EnrollmentCodes.Any(c => c.UsedAt == null && c.ExpiresAt > now) ? ConnectionState.WaitingForTill
         : site.Tokens.Count > 0 ? ConnectionState.Disconnected
         : site.EnrollmentCodes.Count > 0 ? ConnectionState.LinkExpired
         : ConnectionState.NotLinked;

    public static string Label(ConnectionState state) => state switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.WaitingForTill => "Waiting for the till",
        ConnectionState.LinkExpired => "Link expired",
        ConnectionState.Disconnected => "Disconnected",
        _ => "Not linked",
    };

    public static string Help(ConnectionState state) => state switch
    {
        ConnectionState.Connected => "This till has its credential and can sync.",
        ConnectionState.WaitingForTill => "A link was created and has not been used yet.",
        ConnectionState.LinkExpired => "The link was never used. Create a new one.",
        ConnectionState.Disconnected => "The till's credential was removed. Create a new link to reconnect it.",
        _ => "Create a link and send it to the shop owner.",
    };

    public static string Tone(ConnectionState state) => state switch
    {
        ConnectionState.Connected => "ok",
        ConnectionState.WaitingForTill => "warn",
        ConnectionState.NotLinked => "none",
        _ => "bad",
    };
}

/// <summary>One hour of the 24-hour contact strip.</summary>
public sealed record PulseCell(DateTime HourUtc, int Contacts);

/// <summary>The last 24 hours, oldest first, ending with the current hour.</summary>
public sealed record PulseData(IReadOnlyList<PulseCell> Cells)
{
    public int LitHours => Cells.Count(c => c.Contacts > 0);

    public static PulseData Build(IEnumerable<SiteHeartbeat> heartbeats, DateTime nowUtc)
    {
        var thisHour = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
        var byHour = heartbeats.GroupBy(h => h.Hour).ToDictionary(g => g.Key, g => g.Sum(h => h.Contacts));
        var cells = Enumerable.Range(0, 24)
            .Select(i => thisHour.AddHours(i - 23))
            .Select(hour => new PulseCell(hour, byHour.TryGetValue(DateTime.SpecifyKind(hour, DateTimeKind.Unspecified), out var n) ? n : 0))
            .ToList();
        return new PulseData(cells);
    }
}

public sealed record PulseView(PulseData Data, bool Large, PortalFormat Fmt);

public enum AttentionSeverity { Warn, Bad }

public sealed record AttentionItem(AttentionSeverity Severity, string Client, string Reason, string Href, string Action);

/// <summary>What to look at first: the few things that need you, instead of a list to scan.</summary>
public static class AttentionRules
{
    public static List<AttentionItem> For(Client client, DateTime now, PortalFormat fmt)
    {
        var items = new List<AttentionItem>();
        if (!client.IsActive) return items; // switched off on purpose

        var clientHref = $"/Clients/Details/{client.Id}";
        var state = SubscriptionRules.Evaluate(client.Subscription, now);
        var sub = client.Subscription;

        if (state is SubscriptionState.Expired or SubscriptionState.Grace && sub is not null)
            items.Add(new(state == SubscriptionState.Expired ? AttentionSeverity.Bad : AttentionSeverity.Warn, client.Name,
                $"Online ended on {fmt.Day(sub.ExpiresAt)}" + (state == SubscriptionState.Grace ? " (grace week)" : ""), clientHref, "Renew"));
        else if (state == SubscriptionState.Active && sub is not null && sub.ExpiresAt - now < TimeSpan.FromDays(14))
            items.Add(new(AttentionSeverity.Warn, client.Name, $"Online ends {fmt.Until(sub.ExpiresAt)}", clientHref, "Renew"));

        if (client.Sites.Count == 0)
            items.Add(new(AttentionSeverity.Warn, client.Name, "No till added yet", clientHref, "Add till"));

        foreach (var site in client.Sites.Where(s => !s.IsDisabled))
        {
            var href = $"/Sites/Details/{site.Id}";
            var name = client.Sites.Count > 1 ? $"{client.Name} · {site.Name}" : client.Name;
            switch (SiteInsight.Connection(site, now))
            {
                case ConnectionState.WaitingForTill:
                    var sent = site.EnrollmentCodes.Where(c => c.UsedAt == null).Max(c => c.CreatedAt);
                    if (now - sent > TimeSpan.FromDays(2))
                        items.Add(new(AttentionSeverity.Warn, name, $"Link sent {fmt.Ago(sent)}, not used yet", href, "Open"));
                    break;
                case ConnectionState.LinkExpired:
                    items.Add(new(AttentionSeverity.Warn, name, "The link expired unused", href, "New link"));
                    break;
                case ConnectionState.Connected:
                    if (SyncHealthRules.Classify(site.LastSeenAt, now) == SyncHealth.Down)
                        items.Add(new(AttentionSeverity.Bad, name, $"No contact since {fmt.Ago(site.LastSeenAt)}", href, "Open"));
                    break;
            }
        }
        return items;
    }
}

/// <summary>The one place that decides what a state is called and what colour it gets, so every page says the same thing.</summary>
public static class Pills
{
    public static (string Tone, string Label) Plan(SubscriptionState state) => state switch
    {
        SubscriptionState.Active => ("ok", "Active"),
        SubscriptionState.Grace => ("warn", "Grace week"),
        SubscriptionState.Expired => ("bad", "Expired"),
        SubscriptionState.Suspended => ("bad", "Suspended"),
        _ => ("none", "No plan"),
    };

    public static (string Tone, string Label) Health(SyncHealth health) => health switch
    {
        SyncHealth.Ok => ("ok", "In contact"),
        SyncHealth.Stale => ("warn", "Quiet"),
        SyncHealth.Down => ("bad", "No contact"),
        _ => ("none", "Never connected"),
    };
}
