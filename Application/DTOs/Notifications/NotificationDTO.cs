using Core.Entities.Notifications;

namespace Application.DTOs.Notifications
{
    public class NotificationDTO
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // info, warning, error, success
        public string Category { get; set; } = string.Empty; // inventory, system, security, sales
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public string Priority { get; set; } = "normal"; // low, normal, high, urgent
        public string Icon { get; set; } = string.Empty;
        public string ActionUrl { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class AlertDTO
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // low_stock, out_of_stock, security, system
        public string Severity { get; set; } = "warning"; // info, warning, error, critical
        public bool IsActive { get; set; } = true;
        public bool IsDismissed { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DismissedAt { get; set; }
        public string ActionRequired { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty; // Product, Order, User, etc.
        public int? EntityId { get; set; }
        public Dictionary<string, object> Context { get; set; } = new();
    }

    /// <summary>One row of the bell / toast feed: either an Alert or a Notification, in one shape.</summary>
    public class NotificationFeedItemDTO
    {
        /// <summary>Stable across polls ("alert-12" / "notification-5"); the client dedupes toasts on it.</summary>
        public string Key { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // alert | notification
        public int Id { get; set; }
        /// <summary>lowStock, outOfStock, or the notification type (info, warning, error, success). Drives client-side copy and colour.</summary>
        public string Kind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>Values the client plugs into the localized copy for Kind (name, stock, reorder).</summary>
        public Dictionary<string, object> Params { get; set; } = new();
        public int? ProductId { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
        public string ActionUrl { get; set; } = string.Empty;
    }

    public class NotificationFeedDTO
    {
        public int UnreadCount { get; set; }
        public List<NotificationFeedItemDTO> Items { get; set; } = new();
    }

    public class NotificationCreateRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = "info";
        public string Category { get; set; } = "system";
        public string Priority { get; set; } = "normal";
        public string Icon { get; set; } = string.Empty;
        public string ActionUrl { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class AlertCreateRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = "system";
        public string Severity { get; set; } = "warning";
        public string ActionRequired { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public int? EntityId { get; set; }
        public Dictionary<string, object> Context { get; set; } = new();
    }

    public class NotificationStatsDTO
    {
        public int TotalNotifications { get; set; }
        public int UnreadNotifications { get; set; }
        public int TotalAlerts { get; set; }
        public int ActiveAlerts { get; set; }
        public int CriticalAlerts { get; set; }
        public int LowStockAlerts { get; set; }
        public int OutOfStockAlerts { get; set; }
        public List<NotificationDTO> RecentNotifications { get; set; } = new();
        public List<AlertDTO> ActiveAlertsList { get; set; } = new();
    }

    public class NotificationUpdateRequest
    {
        public int Id { get; set; }
        public bool IsRead { get; set; }
    }

    public class AlertUpdateRequest
    {
        public int Id { get; set; }
        public bool IsDismissed { get; set; }
    }

    public class BulkNotificationUpdateRequest
    {
        public List<int> NotificationIds { get; set; } = new();
        public bool IsRead { get; set; }
    }

    public class NotificationFilterRequest
    {
        public string? Type { get; set; }
        public string? Category { get; set; }
        public string? Priority { get; set; }
        public bool? IsRead { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class AlertFilterRequest
    {
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsDismissed { get; set; }
        public string? EntityType { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    /// <summary>A page of rows plus how many rows match the filter in total.</summary>
    public class NotificationPage<T>
    {
        public List<T> Items { get; set; } = new();
        public int Total { get; set; }
    }

    /// <summary>Server-side filter + paging of the notifications list (the SPA's page; the old filter loaded every row first).</summary>
    public class NotificationPageFilter
    {
        public NotificationType? Type { get; set; }
        public bool? IsRead { get; set; }
        /// <summary>Matches title or message.</summary>
        public string? Search { get; set; }
        public bool Descending { get; set; } = true;
        public int Skip { get; set; }
        public int Take { get; set; } = 25;
    }

    public enum AlertScope
    {
        /// <summary>Active or acknowledged: still on the bell.</summary>
        Open,
        /// <summary>Resolved or dismissed.</summary>
        Closed,
        All
    }

    public class AlertPageFilter
    {
        public AlertType? Type { get; set; }
        public AlertScope Scope { get; set; } = AlertScope.Open;
        public string? Search { get; set; }
        public bool Descending { get; set; } = true;
        public int Skip { get; set; }
        public int Take { get; set; } = 25;
    }

    /// <summary>Head-line counts of the notifications page, computed in the database.</summary>
    public class NotificationCountsDTO
    {
        public int TotalNotifications { get; set; }
        public int UnreadNotifications { get; set; }
        public int TotalAlerts { get; set; }
        /// <summary>Alerts nobody has acknowledged yet.</summary>
        public int ActiveAlerts { get; set; }
        public int OpenAlerts { get; set; }
        public int LowStockAlerts { get; set; }
        public int OutOfStockAlerts { get; set; }
    }
}
