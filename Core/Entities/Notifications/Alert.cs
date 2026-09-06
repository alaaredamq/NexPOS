using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Notifications;

public class Alert : IEntityBase<int>
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public AlertType Type { get; set; }
    public AlertStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public int? AcknowledgedByUserId { get; set; }
    public User? AcknowledgedByUser { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
    public User? ResolvedByUser { get; set; }
    public string? Metadata { get; set; } // JSON for additional data
}

public enum AlertType
{
    LowStock = 0,
    OutOfStock = 1,
    ExpiredProduct = 2,
    PaymentFailed = 3,
    SystemError = 4,
    BackupFailed = 5,
    SecurityBreach = 6
}

public enum AlertStatus
{
    Active = 0,
    Acknowledged = 1,
    Resolved = 2,
    Dismissed = 3
}
