using Application.DTOs.Notifications;
using Core.Entities.Notifications;

namespace Application.IServices.Notifications
{
    public interface INotificationService
    {
        // Notification CRUD operations
        Task<IEnumerable<Notification>> GetAllAsync();
        Task<Notification> GetByIdAsync(int id);
        Task<Notification> CreateAsync(Notification entity);
        Task<Notification> UpdateAsync(Notification entity);
        Task DeleteAsync(int id);
        Task DeleteOldNotificationsAsync(int daysOld = 30);

        // Alert CRUD operations
        Task<IEnumerable<Alert>> GetAllAlertsAsync();
        Task<Alert> GetAlertByIdAsync(int id);
        Task<Alert> CreateAlertAsync(Alert entity);
        Task<Alert> UpdateAlertAsync(Alert entity);
        Task DeleteAlertAsync(int id);

        // Notification management
        Task<Notification> CreateNotificationAsync(NotificationCreateRequest request);
        Task<Notification> MarkAsReadAsync(int id);
        Task MarkMultipleAsReadAsync(List<int> ids);
        Task<IEnumerable<Notification>> GetUnreadNotificationsAsync();
        Task<IEnumerable<Notification>> GetNotificationsByCategoryAsync(string category);
        Task<IEnumerable<Notification>> GetRecentNotificationsAsync(int count = 10);

        // Alert management
        Task<Alert> CreateAlertAsync(AlertCreateRequest request);
        Task<Alert> DismissAlertAsync(int id);
        Task<IEnumerable<Alert>> GetActiveAlertsAsync();
        Task<IEnumerable<Alert>> GetAlertsByTypeAsync(string type);
        Task<IEnumerable<Alert>> GetCriticalAlertsAsync();

        // Bell / toast feed. Alerts are only included when includeAlerts is true (caller checks the Alerts permission).
        Task<NotificationFeedDTO> GetFeedAsync(int take, bool includeAlerts);
        Task MarkAlertReadAsync(int alertId, int userId);
        Task MarkAllReadAsync(int userId, bool includeAlerts);
        Task DismissAllAlertsAsync();
        Task ClearAllNotificationsAsync();

        // Statistics and dashboard
        Task<NotificationStatsDTO> GetNotificationStatsAsync();
        Task<int> GetUnreadCountAsync();
        Task<int> GetActiveAlertCountAsync();

        // Filtering and search
        Task<IEnumerable<Notification>> GetFilteredNotificationsAsync(NotificationFilterRequest request);
        Task<IEnumerable<Alert>> GetFilteredAlertsAsync(AlertFilterRequest request);

        // Lookups that answer null instead of throwing, and database-side paging/counts for the SPA's notifications page.
        Task<Notification?> FindNotificationAsync(int id);
        Task<Alert?> FindAlertAsync(int id);
        Task<NotificationPage<Notification>> GetNotificationPageAsync(NotificationPageFilter filter);
        Task<NotificationPage<Alert>> GetAlertPageAsync(AlertPageFilter filter);
        /// <summary>Alert counts are zero unless includeAlerts (the caller checks the Alerts permission).</summary>
        Task<NotificationCountsDTO> GetCountsAsync(bool includeAlerts);

        // Bulk operations
        Task BulkUpdateNotificationsAsync(BulkNotificationUpdateRequest request);
        Task BulkDeleteNotificationsAsync(List<int> ids);
        Task BulkDismissAlertsAsync(List<int> ids);

        // System notifications
        Task CreateLowStockAlertAsync(int productId, int currentStock, int minimumStock);
        Task CreateOutOfStockAlertAsync(int productId);
        Task CreateSecurityAlertAsync(string message, string details);
        Task CreateSystemAlertAsync(string message, string severity = "warning");
        Task CreateSalesAlertAsync(string message, Dictionary<string, object> context = null);

        // Auto-cleanup
        Task CleanupOldNotificationsAsync();
        Task CleanupDismissedAlertsAsync();
    }
}