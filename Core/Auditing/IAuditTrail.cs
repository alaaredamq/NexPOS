using Core.Entities.Logging;

namespace Core.Auditing;

/// <summary>
/// Write side of the audit trail. Everything that should be remembered - a login, a sale, a
/// backup, a changed record - ends up here.
/// </summary>
public interface IAuditTrail
{
    /// <summary>
    /// Queues an entry for the background writer. Missing who/where fields (user, IP, request id,
    /// machine, version) are filled in from the current <see cref="IAuditContext"/>. Never throws and
    /// never blocks the caller: auditing must not be able to fail a sale.
    /// </summary>
    void Record(AuditLog entry);

    /// <summary>Completes once everything queued before the call has been written.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>Short-hands for the explicit (non data-change) events.</summary>
public static class AuditTrailExtensions
{
    public static void LogEvent(
        this IAuditTrail trail,
        AuditCategory category,
        string action,
        string description,
        string entityType,
        AuditOutcome outcome = AuditOutcome.Success,
        AuditLogSeverity severity = AuditLogSeverity.Info,
        int? entityId = null,
        string? entityLabel = null,
        object? details = null,
        int? userId = null,
        string? username = null)
    {
        trail.Record(new AuditLog
        {
            Category = category,
            Action = action,
            Description = description,
            EntityType = entityType,
            Outcome = outcome,
            Severity = severity,
            EntityId = entityId,
            EntityLabel = entityLabel,
            AdditionalInfoJson = AuditRedactor.SerializeDetails(details),
            UserId = userId,
            Username = username
        });
    }

    public static void LogSecurity(
        this IAuditTrail trail,
        string action,
        string description,
        AuditOutcome outcome = AuditOutcome.Success,
        AuditLogSeverity severity = AuditLogSeverity.Info,
        int? entityId = null,
        string? entityLabel = null,
        object? details = null,
        int? userId = null,
        string? username = null)
        => trail.LogEvent(AuditCategory.Security, action, description, "User", outcome, severity,
            entityId, entityLabel, details, userId, username);

    public static void LogBusiness(
        this IAuditTrail trail,
        string action,
        string description,
        string entityType,
        AuditOutcome outcome = AuditOutcome.Success,
        AuditLogSeverity severity = AuditLogSeverity.Info,
        int? entityId = null,
        string? entityLabel = null,
        object? details = null)
        => trail.LogEvent(AuditCategory.Business, action, description, entityType, outcome, severity,
            entityId, entityLabel, details);

    public static void LogSystem(
        this IAuditTrail trail,
        string action,
        string description,
        string entityType = "System",
        AuditOutcome outcome = AuditOutcome.Success,
        AuditLogSeverity severity = AuditLogSeverity.Info,
        object? details = null)
        => trail.LogEvent(AuditCategory.System, action, description, entityType, outcome, severity,
            details: details);
}
