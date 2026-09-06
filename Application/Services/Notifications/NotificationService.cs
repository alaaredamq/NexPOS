using Application.DTOs.Notifications;
using Application.IServices.Notifications;
using Core.Entities.Notifications;
using Core.IRepositories.Notifications;
using System.Text.Json;

namespace Application.Services.Notifications
{
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly IAlertRepository _alertRepository;

        public NotificationService(
            INotificationRepository notificationRepository,
            IAlertRepository alertRepository)
        {
            _notificationRepository = notificationRepository;
            _alertRepository = alertRepository;
        }

        #region Notification CRUD Operations

        public async Task<IEnumerable<Notification>> GetAllAsync()
        {
            return await _notificationRepository.GetAllAsync();
        }

        public async Task<Notification> GetByIdAsync(int id)
        {
            return await _notificationRepository.GetByIdAsync(id);
        }

        public async Task<Notification> CreateAsync(Notification entity)
        {
            entity.CreatedAt = DateTime.UtcNow;
            entity.IsRead = false;
            return await _notificationRepository.SaveAsync(entity);
        }

        public async Task<Notification> UpdateAsync(Notification entity)
        {
            return await _notificationRepository.SaveAsync(entity);
        }

        public async Task DeleteAsync(int id)
        {
            await _notificationRepository.DeleteAsync(id);
        }

        public async Task DeleteOldNotificationsAsync(int daysOld = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
            var oldNotifications = await _notificationRepository.GetAllAsync(n => n.CreatedAt < cutoffDate);
            
            foreach (var notification in oldNotifications)
            {
                await _notificationRepository.DeleteAsync(notification.Id);
            }
        }

        #endregion

        #region Alert CRUD Operations

        public async Task<IEnumerable<Alert>> GetAllAlertsAsync()
        {
            return await _alertRepository.GetAllAsync();
        }

        public async Task<Alert> GetAlertByIdAsync(int id)
        {
            return await _alertRepository.GetByIdAsync(id);
        }

        public async Task<Alert> CreateAlertAsync(Alert entity)
        {
            entity.CreatedAt = DateTime.UtcNow;
            entity.Status = AlertStatus.Active;
            return await _alertRepository.SaveAsync(entity);
        }

        public async Task<Alert> UpdateAlertAsync(Alert entity)
        {
            return await _alertRepository.SaveAsync(entity);
        }

        public async Task DeleteAlertAsync(int id)
        {
            await _alertRepository.DeleteAsync(id);
        }

        #endregion

        #region Notification Management

        public async Task<Notification> CreateNotificationAsync(NotificationCreateRequest request)
        {
            var notification = new Notification
            {
                Title = request.Title,
                Message = request.Message,
                Type = ParseNotificationType(request.Type),
                Priority = ParseNotificationPriority(request.Priority),
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };

            return await CreateAsync(notification);
        }

        public async Task<Notification> MarkAsReadAsync(int id)
        {
            var notification = await GetByIdAsync(id);
            if (notification != null)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                return await UpdateAsync(notification);
            }
            return notification;
        }

        public async Task MarkMultipleAsReadAsync(List<int> ids)
        {
            foreach (var id in ids)
            {
                await MarkAsReadAsync(id);
            }
        }

        public async Task<IEnumerable<Notification>> GetUnreadNotificationsAsync()
        {
            return await _notificationRepository.GetAllAsync(n => !n.IsRead);
        }

        public async Task<IEnumerable<Notification>> GetNotificationsByCategoryAsync(string category)
        {
            // Since Category doesn't exist in the entity, we'll filter by Type instead
            var type = ParseNotificationType(category);
            return await _notificationRepository.GetAllAsync(n => n.Type == type);
        }

        public async Task<IEnumerable<Notification>> GetRecentNotificationsAsync(int count = 10)
        {
            return (await _notificationRepository.GetAllAsync())
                .OrderByDescending(n => n.CreatedAt)
                .Take(count);
        }

        #endregion

        #region Alert Management

        public async Task<Alert> CreateAlertAsync(AlertCreateRequest request)
        {
            var alert = new Alert
            {
                Title = request.Title,
                Message = request.Message,
                Type = ParseAlertType(request.Type),
                Status = AlertStatus.Active,
                Metadata = JsonSerializer.Serialize(request.Context),
                CreatedAt = DateTime.UtcNow
            };

            return await CreateAlertAsync(alert);
        }

        public async Task<Alert> DismissAlertAsync(int id)
        {
            var alert = await GetAlertByIdAsync(id);
            if (alert != null)
            {
                alert.Status = AlertStatus.Dismissed;
                alert.ResolvedAt = DateTime.UtcNow;
                return await UpdateAlertAsync(alert);
            }
            return alert;
        }

        public async Task<IEnumerable<Alert>> GetActiveAlertsAsync()
        {
            return await _alertRepository.GetAllAsync(a => a.Status == AlertStatus.Active);
        }

        public async Task<IEnumerable<Alert>> GetAlertsByTypeAsync(string type)
        {
            var alertType = ParseAlertType(type);
            return await _alertRepository.GetAllAsync(a => a.Type == alertType);
        }

        public async Task<IEnumerable<Alert>> GetCriticalAlertsAsync()
        {
            // Since there's no Severity field, we'll return all active alerts
            return await _alertRepository.GetAllAsync(a => a.Status == AlertStatus.Active);
        }

        #endregion

        #region Statistics and Dashboard

        public async Task<NotificationStatsDTO> GetNotificationStatsAsync()
        {
            var notifications = await GetAllAsync();
            var alerts = await GetAllAlertsAsync();

            var stats = new NotificationStatsDTO
            {
                TotalNotifications = notifications.Count(),
                UnreadNotifications = notifications.Count(n => !n.IsRead),
                TotalAlerts = alerts.Count(),
                ActiveAlerts = alerts.Count(a => a.Status == AlertStatus.Active),
                CriticalAlerts = alerts.Count(a => a.Status == AlertStatus.Active), // All active alerts are considered critical
                LowStockAlerts = alerts.Count(a => a.Type == AlertType.LowStock && a.Status == AlertStatus.Active),
                OutOfStockAlerts = alerts.Count(a => a.Type == AlertType.LowStock && a.Status == AlertStatus.Active), // Using LowStock for out of stock
                RecentNotifications = (await GetRecentNotificationsAsync(5)).Select(MapToDTO).ToList(),
                ActiveAlertsList = (await GetActiveAlertsAsync()).Select(MapAlertToDTO).ToList()
            };

            return stats;
        }

        public async Task<int> GetUnreadCountAsync()
        {
            var unread = await GetUnreadNotificationsAsync();
            return unread.Count();
        }

        public async Task<int> GetActiveAlertCountAsync()
        {
            var active = await GetActiveAlertsAsync();
            return active.Count();
        }

        #endregion

        #region Filtering and Search

        public async Task<IEnumerable<Notification>> GetFilteredNotificationsAsync(NotificationFilterRequest request)
        {
            var notifications = await GetAllAsync();

            if (!string.IsNullOrEmpty(request.Type))
            {
                var type = ParseNotificationType(request.Type);
                notifications = notifications.Where(n => n.Type == type);
            }

            if (!string.IsNullOrEmpty(request.Category))
            {
                var type = ParseNotificationType(request.Category);
                notifications = notifications.Where(n => n.Type == type);
            }

            if (!string.IsNullOrEmpty(request.Priority))
            {
                var priority = ParseNotificationPriority(request.Priority);
                notifications = notifications.Where(n => n.Priority == priority);
            }

            if (request.IsRead.HasValue)
                notifications = notifications.Where(n => n.IsRead == request.IsRead.Value);

            if (request.FromDate.HasValue)
                notifications = notifications.Where(n => n.CreatedAt >= request.FromDate.Value);

            if (request.ToDate.HasValue)
                notifications = notifications.Where(n => n.CreatedAt <= request.ToDate.Value);

            return notifications
                .OrderByDescending(n => n.CreatedAt)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize);
        }

        public async Task<IEnumerable<Alert>> GetFilteredAlertsAsync(AlertFilterRequest request)
        {
            var alerts = await GetAllAlertsAsync();

            if (!string.IsNullOrEmpty(request.Type))
            {
                var type = ParseAlertType(request.Type);
                alerts = alerts.Where(a => a.Type == type);
            }

            if (!string.IsNullOrEmpty(request.Severity))
            {
                // Since there's no Severity field, we'll skip this filter
            }

            if (request.IsActive.HasValue)
            {
                var status = request.IsActive.Value ? AlertStatus.Active : AlertStatus.Dismissed;
                alerts = alerts.Where(a => a.Status == status);
            }

            if (request.IsDismissed.HasValue)
            {
                var status = request.IsDismissed.Value ? AlertStatus.Dismissed : AlertStatus.Active;
                alerts = alerts.Where(a => a.Status == status);
            }

            if (!string.IsNullOrEmpty(request.EntityType))
            {
                // Since there's no EntityType field, we'll skip this filter
            }

            if (request.FromDate.HasValue)
                alerts = alerts.Where(a => a.CreatedAt >= request.FromDate.Value);

            if (request.ToDate.HasValue)
                alerts = alerts.Where(a => a.CreatedAt <= request.ToDate.Value);

            return alerts
                .OrderByDescending(a => a.CreatedAt)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize);
        }

        #endregion

        #region Bulk Operations

        public async Task BulkUpdateNotificationsAsync(BulkNotificationUpdateRequest request)
        {
            await MarkMultipleAsReadAsync(request.NotificationIds);
        }

        public async Task BulkDeleteNotificationsAsync(List<int> ids)
        {
            foreach (var id in ids)
            {
                await DeleteAsync(id);
            }
        }

        public async Task BulkDismissAlertsAsync(List<int> ids)
        {
            foreach (var id in ids)
            {
                await DismissAlertAsync(id);
            }
        }

        #endregion

        #region System Notifications

        public async Task CreateLowStockAlertAsync(int productId, int currentStock, int minimumStock)
        {
            var request = new AlertCreateRequest
            {
                Title = "Low Stock Alert",
                Message = $"Product has low stock: {currentStock} units remaining (minimum: {minimumStock})",
                Type = "low_stock",
                Severity = currentStock <= minimumStock / 2 ? "critical" : "warning",
                ActionRequired = "Restock product inventory",
                EntityType = "Product",
                EntityId = productId,
                Context = new Dictionary<string, object>
                {
                    ["current_stock"] = currentStock,
                    ["minimum_stock"] = minimumStock,
                    ["product_id"] = productId
                }
            };

            await CreateAlertAsync(request);
        }

        public async Task CreateOutOfStockAlertAsync(int productId)
        {
            var request = new AlertCreateRequest
            {
                Title = "Out of Stock Alert",
                Message = "Product is out of stock and needs immediate restocking",
                Type = "out_of_stock",
                Severity = "critical",
                ActionRequired = "Urgent restocking required",
                EntityType = "Product",
                EntityId = productId,
                Context = new Dictionary<string, object>
                {
                    ["product_id"] = productId,
                    ["stock_level"] = 0
                }
            };

            await CreateAlertAsync(request);
        }

        public async Task CreateSecurityAlertAsync(string message, string details)
        {
            var request = new AlertCreateRequest
            {
                Title = "Security Alert",
                Message = message,
                Type = "security",
                Severity = "critical",
                ActionRequired = "Review security logs and take appropriate action",
                EntityType = "System",
                Context = new Dictionary<string, object>
                {
                    ["details"] = details,
                    ["timestamp"] = DateTime.UtcNow
                }
            };

            await CreateAlertAsync(request);
        }

        public async Task CreateSystemAlertAsync(string message, string severity = "warning")
        {
            var request = new AlertCreateRequest
            {
                Title = "System Alert",
                Message = message,
                Type = "system",
                Severity = severity,
                ActionRequired = "Review system status",
                EntityType = "System",
                Context = new Dictionary<string, object>
                {
                    ["timestamp"] = DateTime.UtcNow
                }
            };

            await CreateAlertAsync(request);
        }

        public async Task CreateSalesAlertAsync(string message, Dictionary<string, object> context = null)
        {
            var request = new AlertCreateRequest
            {
                Title = "Sales Alert",
                Message = message,
                Type = "sales",
                Severity = "warning",
                ActionRequired = "Review sales performance",
                EntityType = "Sales",
                Context = context ?? new Dictionary<string, object>()
            };

            await CreateAlertAsync(request);
        }

        #endregion

        #region Auto-cleanup

        public async Task CleanupOldNotificationsAsync()
        {
            await DeleteOldNotificationsAsync(30);
        }

        public async Task CleanupDismissedAlertsAsync()
        {
            var dismissedAlerts = await _alertRepository.GetAllAsync(a => a.Status == AlertStatus.Dismissed);
            var cutoffDate = DateTime.UtcNow.AddDays(-7); // Keep dismissed alerts for 7 days
            
            foreach (var alert in dismissedAlerts.Where(a => a.ResolvedAt < cutoffDate))
            {
                await DeleteAlertAsync(alert.Id);
            }
        }

        #endregion

        #region Helper Methods

        private NotificationDTO MapToDTO(Notification notification)
        {
            return new NotificationDTO
            {
                Id = notification.Id,
                Title = notification.Title,
                Message = notification.Message,
                Type = notification.Type.ToString().ToLower(),
                Category = notification.Type.ToString().ToLower(),
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt,
                ReadAt = notification.ReadAt,
                Priority = notification.Priority.ToString().ToLower(),
                Icon = GetNotificationIcon(notification.Type),
                ActionUrl = string.Empty,
                ActionText = string.Empty,
                Metadata = new Dictionary<string, object>()
            };
        }

        private AlertDTO MapAlertToDTO(Alert alert)
        {
            return new AlertDTO
            {
                Id = alert.Id,
                Title = alert.Title,
                Message = alert.Message,
                Type = alert.Type.ToString().ToLower(),
                Severity = alert.Status == AlertStatus.Active ? "warning" : "info",
                IsActive = alert.Status == AlertStatus.Active,
                IsDismissed = alert.Status == AlertStatus.Dismissed,
                CreatedAt = alert.CreatedAt,
                DismissedAt = alert.ResolvedAt,
                ActionRequired = GetActionRequired(alert.Type),
                EntityType = "System",
                EntityId = null,
                Context = string.IsNullOrEmpty(alert.Metadata) 
                    ? new Dictionary<string, object>() 
                    : JsonSerializer.Deserialize<Dictionary<string, object>>(alert.Metadata) ?? new Dictionary<string, object>()
            };
        }

        private NotificationType ParseNotificationType(string type)
        {
            return type.ToLower() switch
            {
                "info" => NotificationType.Info,
                "warning" => NotificationType.Warning,
                "error" => NotificationType.Error,
                "success" => NotificationType.Success,
                _ => NotificationType.Info
            };
        }

        private NotificationPriority ParseNotificationPriority(string priority)
        {
            return priority.ToLower() switch
            {
                "low" => NotificationPriority.Low,
                "normal" => NotificationPriority.Normal,
                "high" => NotificationPriority.High,
                "critical" => NotificationPriority.Critical,
                _ => NotificationPriority.Normal
            };
        }

        private AlertType ParseAlertType(string type)
        {
            return type.ToLower() switch
            {
                "low_stock" => AlertType.LowStock,
                "expired_product" => AlertType.ExpiredProduct,
                "payment_failed" => AlertType.PaymentFailed,
                "system_error" => AlertType.SystemError,
                "backup_failed" => AlertType.BackupFailed,
                "security_breach" => AlertType.SecurityBreach,
                _ => AlertType.SystemError
            };
        }

        private string GetNotificationIcon(NotificationType type)
        {
            return type switch
            {
                NotificationType.Info => "fas fa-info-circle",
                NotificationType.Warning => "fas fa-exclamation-triangle",
                NotificationType.Error => "fas fa-exclamation-circle",
                NotificationType.Success => "fas fa-check-circle",
                _ => "fas fa-bell"
            };
        }

        private string GetActionRequired(AlertType type)
        {
            return type switch
            {
                AlertType.LowStock => "Restock inventory",
                AlertType.ExpiredProduct => "Remove expired products",
                AlertType.PaymentFailed => "Check payment processing",
                AlertType.SystemError => "Review system logs",
                AlertType.BackupFailed => "Run backup manually",
                AlertType.SecurityBreach => "Review security logs",
                _ => "Review alert"
            };
        }

        #endregion
    }
}