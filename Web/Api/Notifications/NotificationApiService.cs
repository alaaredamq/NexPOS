using Application.DTOs.Notifications;
using Application.IServices.Notifications;
using Application.Services.Notifications;
using Core.Entities.Notifications;
using Web.Api.Infrastructure;
using Web.Api.Notifications.Contracts;

namespace Web.Api.Notifications;

public interface INotificationApiService
{
    Task<NotificationFeedDto> FeedAsync(int take, bool includeAlerts);

    Task<NotificationSummaryDto> SummaryAsync(bool includeAlerts);

    /// <summary>Throws <see cref="NotificationRuleException"/> for an unknown <paramref name="kind"/>.</summary>
    Task<PagedResult<NotificationRowDto>> ListNotificationsAsync(PagedQuery query, string? kind, bool unreadOnly);

    /// <summary>Throws <see cref="NotificationRuleException"/> for an unknown <paramref name="kind"/> or <paramref name="scope"/>.</summary>
    Task<PagedResult<AlertRowDto>> ListAlertsAsync(PagedQuery query, string? kind, string? scope);

    /// <summary>False when the notification does not exist. Already-read notifications keep their original read time.</summary>
    Task<bool> MarkNotificationReadAsync(int id);

    /// <summary>The bell's click: marks a notification read, or acknowledges an alert as <paramref name="userId"/>. False when it does not exist.</summary>
    Task<bool> MarkFeedItemReadAsync(string source, int id, int userId);

    Task MarkAllReadAsync(int userId, bool includeAlerts);

    Task<bool> DeleteNotificationAsync(int id);

    Task ClearNotificationsAsync();

    /// <summary>Deletes notifications older than 30 days and alerts dismissed more than 7 days ago (the old "Cleanup" button).</summary>
    Task CleanupAsync();

    Task<bool> DismissAlertAsync(int id);

    Task DismissAllAlertsAsync();

    Task<bool> DeleteAlertAsync(int id);
}

/// <summary>A filter value the API does not know; <see cref="Field"/> is the query-string name.</summary>
public sealed class NotificationRuleException : Exception
{
    public string Field { get; }

    public NotificationRuleException(string field, string message) : base(message) => Field = field;
}

/// <summary>Maps the shared notification service to the API's DTOs. The rules (what counts as unread, what a dismiss does) stay in that service.</summary>
public sealed class NotificationApiService : INotificationApiService
{
    public const string SourceAlert = "alert";
    public const string SourceNotification = "notification";

    private readonly INotificationService _notifications;

    public NotificationApiService(INotificationService notifications)
    {
        _notifications = notifications;
    }

    public async Task<NotificationFeedDto> FeedAsync(int take, bool includeAlerts)
    {
        var feed = await _notifications.GetFeedAsync(take, includeAlerts);
        return new NotificationFeedDto { UnreadCount = feed.UnreadCount, Items = feed.Items.Select(ToFeedItem).ToList() };
    }

    public async Task<NotificationSummaryDto> SummaryAsync(bool includeAlerts)
    {
        var counts = await _notifications.GetCountsAsync(includeAlerts);
        return new NotificationSummaryDto
        {
            TotalNotifications = counts.TotalNotifications,
            UnreadNotifications = counts.UnreadNotifications,
            AlertsVisible = includeAlerts,
            TotalAlerts = counts.TotalAlerts,
            ActiveAlerts = counts.ActiveAlerts,
            OpenAlerts = counts.OpenAlerts,
            LowStockAlerts = counts.LowStockAlerts,
            OutOfStockAlerts = counts.OutOfStockAlerts,
        };
    }

    public async Task<PagedResult<NotificationRowDto>> ListNotificationsAsync(PagedQuery query, string? kind, bool unreadOnly)
    {
        var page = await _notifications.GetNotificationPageAsync(new NotificationPageFilter
        {
            Type = ParseEnum<NotificationType>(kind, "kind"),
            IsRead = unreadOnly ? false : null,
            Search = query.Q,
            // Newest first unless the caller asks for oldest first; createdAt is the only sort.
            Descending = !string.Equals(query.Dir, "asc", StringComparison.OrdinalIgnoreCase),
            Skip = query.Skip,
            Take = query.PageSize,
        });

        return PagedResult<NotificationRowDto>.Of(page.Items.Select(ToRow).ToList(), page.Total, query);
    }

    public async Task<PagedResult<AlertRowDto>> ListAlertsAsync(PagedQuery query, string? kind, string? scope)
    {
        var page = await _notifications.GetAlertPageAsync(new AlertPageFilter
        {
            Type = ParseEnum<AlertType>(kind, "kind"),
            Scope = ParseEnum<AlertScope>(scope, "scope") ?? AlertScope.Open,
            Search = query.Q,
            Descending = !string.Equals(query.Dir, "asc", StringComparison.OrdinalIgnoreCase),
            Skip = query.Skip,
            Take = query.PageSize,
        });

        return PagedResult<AlertRowDto>.Of(page.Items.Select(ToRow).ToList(), page.Total, query);
    }

    public async Task<bool> MarkNotificationReadAsync(int id)
    {
        var notification = await _notifications.FindNotificationAsync(id);
        if (notification is null) return false;
        if (!notification.IsRead) await _notifications.MarkAsReadAsync(id);
        return true;
    }

    public async Task<bool> MarkFeedItemReadAsync(string source, int id, int userId)
    {
        if (source == SourceAlert)
        {
            if (await _notifications.FindAlertAsync(id) is null) return false;
            await _notifications.MarkAlertReadAsync(id, userId);
            return true;
        }

        return await MarkNotificationReadAsync(id);
    }

    public Task MarkAllReadAsync(int userId, bool includeAlerts) => _notifications.MarkAllReadAsync(userId, includeAlerts);

    public async Task<bool> DeleteNotificationAsync(int id)
    {
        if (await _notifications.FindNotificationAsync(id) is null) return false;
        await _notifications.DeleteAsync(id);
        return true;
    }

    public Task ClearNotificationsAsync() => _notifications.ClearAllNotificationsAsync();

    public async Task CleanupAsync()
    {
        await _notifications.CleanupOldNotificationsAsync();
        await _notifications.CleanupDismissedAlertsAsync();
    }

    public async Task<bool> DismissAlertAsync(int id)
    {
        if (await _notifications.FindAlertAsync(id) is null) return false;
        await _notifications.DismissAlertAsync(id);
        return true;
    }

    public Task DismissAllAlertsAsync() => _notifications.DismissAllAlertsAsync();

    public async Task<bool> DeleteAlertAsync(int id)
    {
        if (await _notifications.FindAlertAsync(id) is null) return false;
        await _notifications.DeleteAlertAsync(id);
        return true;
    }

    private static T? ParseEnum<T>(string? value, string field) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Accepts the camelCase the API sends back (lowStock) as well as the enum name; numbers are not a filter.
        if (!int.TryParse(value, out _) && Enum.TryParse<T>(value, ignoreCase: true, out var parsed)) return parsed;
        throw new NotificationRuleException(field, $"Unknown {field} '{value}'.");
    }

    private static FeedItemDto ToFeedItem(NotificationFeedItemDTO item) => new()
    {
        Key = item.Key,
        Source = item.Source,
        Id = item.Id,
        Kind = item.Kind,
        Title = item.Title,
        Message = item.Message,
        Params = item.Params,
        ProductId = item.ProductId,
        CreatedAt = item.CreatedAt,
        IsRead = item.IsRead,
    };

    private static NotificationRowDto ToRow(Notification n) => new()
    {
        Id = n.Id,
        Kind = n.Type.ToString().ToLowerInvariant(),
        Priority = n.Priority.ToString().ToLowerInvariant(),
        Title = n.Title,
        Message = n.Message,
        IsRead = n.IsRead,
        CreatedAt = n.CreatedAt,
        ReadAt = n.ReadAt,
    };

    private static AlertRowDto ToRow(Alert a)
    {
        // Same product facts, kind and deep link as the bell, so the list and the bell word an alert identically.
        var facts = NotificationService.MapAlertToFeedItem(a);
        return new AlertRowDto
        {
            Id = a.Id,
            Kind = facts.Kind,
            Status = a.Status.ToString().ToLowerInvariant(),
            Title = a.Title,
            Message = a.Message,
            Params = facts.Params,
            ProductId = facts.ProductId,
            CreatedAt = a.CreatedAt,
            AcknowledgedAt = a.AcknowledgedAt,
            ResolvedAt = a.ResolvedAt,
        };
    }
}
