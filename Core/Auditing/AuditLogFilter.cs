using Core.Entities.Logging;

namespace Core.Auditing;

/// <summary>Everything the audit viewer can filter on. Null/empty members are ignored.</summary>
public sealed class AuditLogFilter
{
    /// <summary>Inclusive lower bound (UTC).</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>Exclusive upper bound (UTC).</summary>
    public DateTime? ToUtc { get; set; }

    public int? UserId { get; set; }
    public string? Username { get; set; }
    public AuditCategory? Category { get; set; }
    public AuditOutcome? Outcome { get; set; }

    /// <summary>Only rows at this severity or worse.</summary>
    public AuditLogSeverity? MinSeverity { get; set; }

    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? Action { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>Free text matched against the description, label, user, action, entity, path, IP and request id.</summary>
    public string? Search { get; set; }

    /// <summary>Oldest first instead of newest first.</summary>
    public bool Ascending { get; set; }
}

/// <summary>Headline numbers for the top of the viewer.</summary>
public sealed class AuditLogStats
{
    public int Total { get; set; }
    public int Last24Hours { get; set; }
    public int Failures24Hours { get; set; }
    public int Security24Hours { get; set; }
    public DateTime? OldestUtc { get; set; }
}

/// <summary>The distinct values that populate the viewer's drop-downs.</summary>
public sealed class AuditLogFilterOptions
{
    public List<AuditUserOption> Users { get; set; } = [];
    public List<string> EntityTypes { get; set; } = [];
    public List<string> Actions { get; set; } = [];
}

public sealed record AuditUserOption(int? UserId, string Username);
