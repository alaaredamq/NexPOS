using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Notifications;

public class Notification : IEntityBase<int>
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public NotificationPriority Priority { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    // Present in the real database but missing here until A15's reconciliation - added to match.
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
}

public enum NotificationType
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Success = 3
}

public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}