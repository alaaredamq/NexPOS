namespace Web.Api.Notifications.Contracts;

/// <summary>One notification of the notifications list. Notifications are shared by every signed-in user (they have no owner in practice).</summary>
public sealed record NotificationRowDto
{
    public required int Id { get; init; }

    /// <summary><c>info</c>, <c>warning</c>, <c>error</c> or <c>success</c>.</summary>
    public required string Kind { get; init; }

    /// <summary><c>low</c>, <c>normal</c>, <c>high</c> or <c>critical</c>.</summary>
    public required string Priority { get; init; }

    /// <summary>As stored (English: written by the code that raised it).</summary>
    public required string Title { get; init; }

    public required string Message { get; init; }

    public required bool IsRead { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC; null while unread.</summary>
    public DateTime? ReadAt { get; init; }
}

/// <summary>One alert. Alerts are raised by the system (stock checks after a sale); there is no screen to create one.</summary>
public sealed record AlertRowDto
{
    public required int Id { get; init; }

    /// <summary>
    /// <c>lowStock</c>, <c>outOfStock</c>, <c>expiredProduct</c>, <c>paymentFailed</c>, <c>systemError</c>, <c>backupFailed</c> or <c>securityBreach</c>.
    /// The UI builds the localized title / message / advice from this plus <see cref="Params"/>; <see cref="Title"/> and <see cref="Message"/> are the stored English fallback.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary><c>active</c> (nobody has looked yet), <c>acknowledged</c>, <c>resolved</c> or <c>dismissed</c>.</summary>
    public required string Status { get; init; }

    public required string Title { get; init; }

    public required string Message { get; init; }

    /// <summary>Product facts stored with a stock alert (<c>productName</c>, <c>stockQuantity</c>, <c>minimumStock</c>); empty for other alerts.</summary>
    public required IReadOnlyDictionary<string, object> Params { get; init; }

    /// <summary>The product a stock alert is about, for the deep link.</summary>
    public int? ProductId { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC; when somebody acknowledged it.</summary>
    public DateTime? AcknowledgedAt { get; init; }

    /// <summary>UTC; when it was resolved or dismissed.</summary>
    public DateTime? ResolvedAt { get; init; }
}

/// <summary>A row of the top-bar bell: an alert or a notification in one shape.</summary>
public sealed record FeedItemDto
{
    /// <summary>Stable across polls (<c>alert-12</c> / <c>notification-5</c>): the UI dedupes toasts on it.</summary>
    public required string Key { get; init; }

    /// <summary><c>alert</c> or <c>notification</c>.</summary>
    public required string Source { get; init; }

    public required int Id { get; init; }

    /// <summary>An alert kind (<c>lowStock</c> ...) or a notification kind (<c>info</c> ...).</summary>
    public required string Kind { get; init; }

    public required string Title { get; init; }

    public required string Message { get; init; }

    public required IReadOnlyDictionary<string, object> Params { get; init; }

    public int? ProductId { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    public required bool IsRead { get; init; }
}

/// <summary>What the bell polls: the newest items and the unread total.</summary>
public sealed record NotificationFeedDto
{
    /// <summary>Unread notifications, plus alerts nobody has acknowledged when the caller may see alerts.</summary>
    public required int UnreadCount { get; init; }

    public required IReadOnlyList<FeedItemDto> Items { get; init; }
}

/// <summary>The tiles of the notifications page.</summary>
public sealed record NotificationSummaryDto
{
    public required int TotalNotifications { get; init; }

    public required int UnreadNotifications { get; init; }

    /// <summary>False when the caller lacks <c>Alerts.Read</c>: the alert figures below are then zero and the page hides its alerts tab.</summary>
    public required bool AlertsVisible { get; init; }

    public required int TotalAlerts { get; init; }

    /// <summary>Alerts nobody has acknowledged yet.</summary>
    public required int ActiveAlerts { get; init; }

    /// <summary>Active or acknowledged: what the alerts tab lists by default.</summary>
    public required int OpenAlerts { get; init; }

    public required int LowStockAlerts { get; init; }

    public required int OutOfStockAlerts { get; init; }
}
