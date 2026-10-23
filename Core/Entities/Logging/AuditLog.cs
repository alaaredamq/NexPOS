using Core.Entities.Base;

namespace Core.Entities.Logging;

/// <summary>
/// One line of the audit trail: who did what, when, from where, and with what result.
/// Rows are append-only - nothing in the application updates or deletes them (only the optional
/// retention purge removes old rows, and that is itself audited). They live in the dedicated audit
/// database (nexpos-audit.db), not in the business database, so a restore of the business data can
/// never erase the history and audit writes never contend with point-of-sale transactions.
/// There are deliberately no foreign keys: an entry must outlive the user/record it describes, so
/// the user's name and role are copied onto the row.
/// </summary>
public class AuditLog : BaseEntity, IEntityBase<int>
{
    // WHEN: CreatedAt (UTC, from BaseEntity)

    // WHO
    public int? UserId { get; set; }
    /// <summary>Name of the acting user at the time ("System" for background work, null if nobody was signed in).</summary>
    public string? Username { get; set; }
    public string? UserRole { get; set; }

    // WHAT
    public AuditCategory Category { get; set; }
    /// <summary>Verb, e.g. Create / Update / Delete / Login / "POST POS/Checkout".</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Entity (table) name for data changes, otherwise the subsystem (Http, Auth, Backup, ...).</summary>
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    /// <summary>Display name of the record at the time (product name, username, receipt number, ...).</summary>
    public string? EntityLabel { get; set; }
    /// <summary>One human-readable sentence describing the event.</summary>
    public string? Description { get; set; }
    public AuditOutcome Outcome { get; set; } = AuditOutcome.Success;
    public AuditLogSeverity Severity { get; set; } = AuditLogSeverity.Info;

    // DETAIL
    /// <summary>Comma-separated names of the properties that changed (updates only).</summary>
    public string? ChangedFields { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    /// <summary>Anything else worth knowing: request parameters, exception details, SQL, ...</summary>
    public string? AdditionalInfoJson { get; set; }

    // WHERE / HOW
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public string? AppVersion { get; set; }
    /// <summary>Groups every row produced by one HTTP request (the same id the error page shows as "Request ID").</summary>
    public string? CorrelationId { get; set; }
    /// <summary>"Web" for user-driven work, "System:&lt;job&gt;" for background work.</summary>
    public string? Source { get; set; }
    public string? HttpMethod { get; set; }
    public string? RequestPath { get; set; }
    public int? StatusCode { get; set; }
    public int? DurationMs { get; set; }
}

/// <summary>What kind of event a row describes.</summary>
public enum AuditCategory
{
    /// <summary>A record was created, changed or deleted.</summary>
    Data = 0,
    /// <summary>An HTTP request reached the application.</summary>
    Request = 1,
    /// <summary>Sign-in, sign-out, PIN changes, denied access.</summary>
    Security = 2,
    /// <summary>A business operation: sale, print, export.</summary>
    Business = 3,
    /// <summary>Application lifecycle, migrations, backups, background jobs.</summary>
    System = 4,
    /// <summary>A warning or error the application logged, tied to the user/request that caused it.</summary>
    Diagnostic = 5
}

public enum AuditOutcome
{
    Success = 0,
    Failure = 1,
    /// <summary>The user was not allowed to do it.</summary>
    Denied = 2
}

public enum AuditLogSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}
