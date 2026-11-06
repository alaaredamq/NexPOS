using Cloud.Data;

namespace Cloud.Services;

public enum SubscriptionState { None, Active, Grace, Expired, Suspended }

/// <summary>
/// Mirrors the till's own licensing philosophy: warn first, never cut anyone off abruptly, never touch their data.
/// After expiry the mirror keeps accepting data for a grace week, then pushes are refused (HTTP 402) and the
/// till simply queues its changes. Selling at the till is not affected at any point.
/// </summary>
public static class SubscriptionRules
{
    public const int GraceDays = 7;

    public static SubscriptionState Evaluate(Subscription? subscription, DateTime nowUtc)
    {
        if (subscription is null) return SubscriptionState.None;
        if (subscription.IsSuspended) return SubscriptionState.Suspended;
        if (nowUtc <= subscription.ExpiresAt) return SubscriptionState.Active;
        return nowUtc <= subscription.ExpiresAt.AddDays(GraceDays) ? SubscriptionState.Grace : SubscriptionState.Expired;
    }

    public static bool AllowsSync(SubscriptionState state) => state is SubscriptionState.Active or SubscriptionState.Grace;

    /// <summary>Renewing early keeps the remaining time; renewing late starts from today, so nobody is charged for a gap.</summary>
    public static DateTime ExtendedExpiry(Subscription subscription, int months, DateTime nowUtc)
        => (subscription.ExpiresAt > nowUtc ? subscription.ExpiresAt : nowUtc).AddMonths(months);
}

public enum SyncHealth { Never, Ok, Stale, Down }

public static class SyncHealthRules
{
    public static SyncHealth Classify(DateTime? lastSyncUtc, DateTime nowUtc)
    {
        if (lastSyncUtc is null) return SyncHealth.Never;
        var age = nowUtc - lastSyncUtc.Value;
        // A connected till checks in at least every 10 minutes even when the shop is quiet, so 25 minutes of silence is a real signal.
        if (age <= TimeSpan.FromMinutes(25)) return SyncHealth.Ok;
        return age <= TimeSpan.FromHours(24) ? SyncHealth.Stale : SyncHealth.Down;
    }
}
