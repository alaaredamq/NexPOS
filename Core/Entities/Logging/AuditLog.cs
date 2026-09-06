using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Logging;

public class AuditLog : ISSoftDeletable, IEntityBase<int>
{
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public int? EntityId { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string IpAddress { get; set; } = null!;
    public string UserAgent { get; set; } = null!;
    public string? AdditionalInfoJson { get; set; }
    public AuditLogSeverity Severity { get; set; } = AuditLogSeverity.Info;

    public AuditLog(int? userId, string action, string entityType, int? entityId, string ipAddress, string userAgent, 
        string? oldValuesJson = null, string? newValuesJson = null, string? additionalInfoJson = null, 
        AuditLogSeverity severity = AuditLogSeverity.Info)
    {
        UserId = userId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        OldValuesJson = oldValuesJson;
        NewValuesJson = newValuesJson;
        AdditionalInfoJson = additionalInfoJson;
        Severity = severity;
    }

    protected AuditLog() { }
}

public enum AuditLogSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}


