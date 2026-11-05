namespace Web.Api.AuditLogs.Contracts;

/// <summary>One row of the audit log table.</summary>
public sealed record AuditLogDto
{
    public required int Id { get; init; }

    /// <summary>When it happened (UTC).</summary>
    public required DateTime Time { get; init; }

    /// <summary>Null when nobody was signed in, and for actors that only exist as a name (see <see cref="User"/>).</summary>
    public int? UserId { get; init; }

    /// <summary>The acting user's name at the time; <c>System</c> for background work. Null when nobody was signed in.</summary>
    public string? User { get; init; }

    public string? Role { get; init; }

    /// <summary><c>Data</c>, <c>Request</c>, <c>Security</c>, <c>Business</c>, <c>System</c> or <c>Diagnostic</c>.</summary>
    public required string Category { get; init; }

    /// <summary>The part of the product it happened in: <c>Sales</c>, <c>Pos</c>, <c>Inventory</c>, <c>Customers</c>, <c>Account</c>, <c>Settings</c>, <c>Reports</c>, <c>System</c> or <c>Other</c>.</summary>
    public required string Area { get; init; }

    /// <summary>Verb or event name, e.g. <c>Update</c>, <c>Login</c>, <c>POST Api.Pos/Checkout</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The table for data changes, otherwise the subsystem (<c>Http</c>, <c>Auth</c>, <c>Backup</c> ...).</summary>
    public required string EntityType { get; init; }

    public int? EntityId { get; init; }

    /// <summary>Display name of the record at the time (product name, receipt number ...).</summary>
    public string? Label { get; init; }

    /// <summary>One sentence describing the event. Whatever language it was written in; users typed parts of it.</summary>
    public string? Description { get; init; }

    /// <summary><c>Success</c>, <c>Failure</c> or <c>Denied</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary><c>Info</c>, <c>Warning</c>, <c>Error</c> or <c>Critical</c>.</summary>
    public required string Severity { get; init; }

    /// <summary>HTTP status of the request, when the entry belongs to one.</summary>
    public int? StatusCode { get; init; }

    public int? DurationMs { get; init; }
}

/// <summary>One field of a record that changed. A create has only <see cref="NewValue"/>, a delete only <see cref="OldValue"/>.</summary>
public sealed record AuditFieldChangeDto
{
    public required string Field { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }
}

/// <summary>An entry with everything the details drawer shows.</summary>
public sealed record AuditLogDetailsDto
{
    public required AuditLogDto Entry { get; init; }

    /// <summary>Field-by-field changes (updates), the saved values (creates) or the removed values (deletes). Empty for requests, sign-ins and the like.</summary>
    public required IReadOnlyList<AuditFieldChangeDto> Changes { get; init; }

    /// <summary>Request parameters, exception text and so on, as pretty-printed JSON. Null when there is none.</summary>
    public string? Additional { get; init; }

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }

    /// <summary>The computer the entry was written on.</summary>
    public string? Machine { get; init; }

    public string? AppVersion { get; init; }

    /// <summary>Groups every row one HTTP request produced (the "Request ID" the error page shows).</summary>
    public string? CorrelationId { get; init; }

    /// <summary><c>Web</c> for user-driven work, <c>System:job</c> for background work.</summary>
    public string? Source { get; init; }

    public string? HttpMethod { get; init; }

    public string? Path { get; init; }

    /// <summary>Rows in total that belong to the same request, this one included (1 when it has no request id).</summary>
    public required int RowsInSameRequest { get; init; }
}

/// <summary>The four tiles above the table. Counts cover the whole log, not the current filter.</summary>
public sealed record AuditLogStatsDto
{
    public required int Total { get; init; }

    public required int Last24Hours { get; init; }

    /// <summary>Failed and denied entries in the last 24 hours.</summary>
    public required int Failures24Hours { get; init; }

    public required int Security24Hours { get; init; }

    /// <summary>Time of the oldest entry (UTC); null when the log is empty.</summary>
    public DateTime? OldestUtc { get; init; }
}

/// <summary>A user (or an actor that only exists as a name) that appears in the log.</summary>
public sealed record AuditUserOptionDto
{
    /// <summary>Null for actors without an account: <c>System</c>, or the name typed into a failed sign-in. Filter those with <c>username</c>.</summary>
    public int? UserId { get; init; }

    public required string Username { get; init; }
}

/// <summary>The distinct values that fill the filter drop-downs.</summary>
public sealed record AuditLogOptionsDto
{
    public required IReadOnlyList<AuditUserOptionDto> Users { get; init; }

    public required IReadOnlyList<string> EntityTypes { get; init; }

    public required IReadOnlyList<string> Actions { get; init; }
}
