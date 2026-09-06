using Application.DTOs.Notifications;
using Application.IServices.Notifications;
using Core.Entities.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;

namespace Web.Controllers
{
    [Authorize]
    public class NotificationsController : BaseController
    {
        private readonly INotificationService _notificationService;

        public NotificationsController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var stats = await _notificationService.GetNotificationStatsAsync();
                return View(stats);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error loading notifications: {ex.Message}";
                return View(new NotificationStatsDTO());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] NotificationFilterRequest request)
        {
            try
            {
                var notifications = await _notificationService.GetFilteredNotificationsAsync(request);
                var notificationDTOs = notifications.Select(n => new NotificationDTO
                {
                    Id = n.Id,
                    Title = n.Title,
                    Message = n.Message,
                    Type = n.Type.ToString().ToLower(),
                    Category = n.Type.ToString().ToLower(),
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt,
                    ReadAt = n.ReadAt,
                    Priority = n.Priority.ToString().ToLower(),
                    Icon = GetNotificationIcon(n.Type),
                    ActionUrl = string.Empty,
                    ActionText = string.Empty
                });

                return Json(new { success = true, data = notificationDTOs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAlerts([FromQuery] AlertFilterRequest request)
        {
            try
            {
                var alerts = await _notificationService.GetFilteredAlertsAsync(request);
                var alertDTOs = alerts.Select(a => new AlertDTO
                {
                    Id = a.Id,
                    Title = a.Title,
                    Message = a.Message,
                    Type = a.Type.ToString().ToLower(),
                    Severity = a.Status == Core.Entities.Notifications.AlertStatus.Active ? "warning" : "info",
                    IsActive = a.Status == Core.Entities.Notifications.AlertStatus.Active,
                    IsDismissed = a.Status == Core.Entities.Notifications.AlertStatus.Dismissed,
                    CreatedAt = a.CreatedAt,
                    DismissedAt = a.ResolvedAt,
                    ActionRequired = GetActionRequired(a.Type),
                    EntityType = "System",
                    EntityId = null
                });

                return Json(new { success = true, data = alertDTOs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> MarkAsRead([FromBody] NotificationUpdateRequest request)
        {
            try
            {
                await _notificationService.MarkAsReadAsync(request.Id);
                return Json(new { success = true, message = "Notification marked as read" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> MarkMultipleAsRead([FromBody] BulkNotificationUpdateRequest request)
        {
            try
            {
                await _notificationService.BulkUpdateNotificationsAsync(request);
                return Json(new { success = true, message = "Notifications marked as read" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DismissAlert([FromBody] AlertUpdateRequest request)
        {
            try
            {
                await _notificationService.DismissAlertAsync(request.Id);
                return Json(new { success = true, message = "Alert dismissed" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> BulkDismissAlerts([FromBody] List<int> alertIds)
        {
            try
            {
                await _notificationService.BulkDismissAlertsAsync(alertIds);
                return Json(new { success = true, message = "Alerts dismissed" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadCount()
        {
            try
            {
                var count = await _notificationService.GetUnreadCountAsync();
                return Json(new { success = true, count = count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveAlertCount()
        {
            try
            {
                var count = await _notificationService.GetActiveAlertCountAsync();
                return Json(new { success = true, count = count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var stats = await _notificationService.GetNotificationStatsAsync();
                return Json(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRecentNotifications()
        {
            try
            {
                var notifications = await _notificationService.GetRecentNotificationsAsync(10);
                var notificationDTOs = notifications.Select(n => new NotificationDTO
                {
                    Id = n.Id,
                    Title = n.Title,
                    Message = n.Message,
                    Type = n.Type.ToString().ToLower(),
                    Category = n.Type.ToString().ToLower(),
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt,
                    Priority = n.Priority.ToString().ToLower(),
                    Icon = GetNotificationIcon(n.Type),
                    ActionUrl = string.Empty,
                    ActionText = string.Empty
                });

                return Json(new { success = true, data = notificationDTOs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveAlerts()
        {
            try
            {
                var alerts = await _notificationService.GetActiveAlertsAsync();
                var alertDTOs = alerts.Select(a => new AlertDTO
                {
                    Id = a.Id,
                    Title = a.Title,
                    Message = a.Message,
                    Type = a.Type.ToString().ToLower(),
                    Severity = a.Status == Core.Entities.Notifications.AlertStatus.Active ? "warning" : "info",
                    IsActive = a.Status == Core.Entities.Notifications.AlertStatus.Active,
                    CreatedAt = a.CreatedAt,
                    ActionRequired = GetActionRequired(a.Type),
                    EntityType = "System",
                    EntityId = null
                });

                return Json(new { success = true, data = alertDTOs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateNotification([FromBody] NotificationCreateRequest request)
        {
            try
            {
                var notification = await _notificationService.CreateNotificationAsync(request);
                return Json(new { success = true, data = notification, message = "Notification created successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateAlert([FromBody] AlertCreateRequest request)
        {
            try
            {
                var alert = await _notificationService.CreateAlertAsync(request);
                return Json(new { success = true, data = alert, message = "Alert created successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Cleanup()
        {
            try
            {
                await _notificationService.CleanupOldNotificationsAsync();
                await _notificationService.CleanupDismissedAlertsAsync();
                return Json(new { success = true, message = "Cleanup completed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteNotification(int id)
        {
            try
            {
                await _notificationService.DeleteAsync(id);
                return Json(new { success = true, message = "Notification deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteAlert(int id)
        {
            try
            {
                await _notificationService.DeleteAlertAsync(id);
                return Json(new { success = true, message = "Alert deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string GetNotificationIcon(Core.Entities.Notifications.NotificationType type)
        {
            return type switch
            {
                Core.Entities.Notifications.NotificationType.Info => "fas fa-info-circle",
                Core.Entities.Notifications.NotificationType.Warning => "fas fa-exclamation-triangle",
                Core.Entities.Notifications.NotificationType.Error => "fas fa-exclamation-circle",
                Core.Entities.Notifications.NotificationType.Success => "fas fa-check-circle",
                _ => "fas fa-bell"
            };
        }

        private string GetActionRequired(Core.Entities.Notifications.AlertType type)
        {
            return type switch
            {
                Core.Entities.Notifications.AlertType.LowStock => "Restock inventory",
                Core.Entities.Notifications.AlertType.ExpiredProduct => "Remove expired products",
                Core.Entities.Notifications.AlertType.PaymentFailed => "Check payment processing",
                Core.Entities.Notifications.AlertType.SystemError => "Review system logs",
                Core.Entities.Notifications.AlertType.BackupFailed => "Run backup manually",
                Core.Entities.Notifications.AlertType.SecurityBreach => "Review security logs",
                _ => "Review alert"
            };
        }
    }
}