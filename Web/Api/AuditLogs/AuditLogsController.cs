using System.ComponentModel.DataAnnotations;
using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.AuditLogs.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.AuditLogs;

/// <summary>
/// The audit trail viewer for support and maintenance: search and filter everything that happened, open an entry to see
/// exactly what changed, follow a request or a record through the log, export it to CSV. Strictly read-only - there is no
/// way to add, edit or delete an entry from here (or from anywhere else). Mirrors the Razor <c>AuditLogsController</c>.
/// </summary>
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController : ApiControllerBase
{
    private readonly IAuditLogApiService _logs;

    public AuditLogsController(IAuditLogApiService logs) => _logs = logs;

    /// <summary>
    /// One page of entries, newest first (<c>dir=asc</c> for oldest first - the only sort there is, so <c>sort</c> may be left out
    /// or be <c>time</c>). <c>q</c> is free text matched against the description, record label, user, action, record type, path,
    /// IP address and request id. Every other filter is optional and they combine with AND.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.AuditLogs_View)]
    [ProducesResponseType<PagedResult<AuditLogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "listAuditLogs")]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List(
        [FromQuery] PagedQuery query, [FromQuery] AuditLogFilterQuery filter, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(query.Sort) && !string.Equals(query.Sort, "time", StringComparison.OrdinalIgnoreCase))
            return ValidationFailure("sort", "Entries can only be sorted by time.");

        var parsed = filter.ToFilter(query.Q, ascending: string.Equals(query.Dir, "asc", StringComparison.OrdinalIgnoreCase), out var badField);
        return badField is null ? await _logs.ListAsync(parsed, query, cancellationToken) : ValidationFailure(badField, "Unknown value.");
    }

    /// <summary>Headline numbers for the tiles (the whole log, not the current filter).</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionHelper.Permissions.AuditLogs_View)]
    [ProducesResponseType<AuditLogStatsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getAuditLogSummary")]
    public async Task<AuditLogStatsDto> Summary(CancellationToken cancellationToken) => await _logs.StatsAsync(cancellationToken);

    /// <summary>The users, record types and actions that appear in the log - the contents of the filter drop-downs.</summary>
    [HttpGet("options")]
    [RequirePermission(PermissionHelper.Permissions.AuditLogs_View)]
    [ProducesResponseType<AuditLogOptionsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getAuditLogOptions")]
    public async Task<AuditLogOptionsDto> Options(CancellationToken cancellationToken) => await _logs.OptionsAsync(cancellationToken);

    /// <summary>Everything about one entry: who, what, where, the field-by-field changes and the raw details.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.AuditLogs_View)]
    [ProducesResponseType<AuditLogDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getAuditLog")]
    public async Task<ActionResult<AuditLogDetailsDto>> Get(int id, CancellationToken cancellationToken)
        => await _logs.GetAsync(id, cancellationToken) is { } details ? details : NotFound();

    /// <summary>
    /// The matching entries as a CSV download (up to 100,000 rows, streamed as they are read). Takes the same filters as the list;
    /// <c>dir</c> and <c>q</c> work as there, <c>page</c>, <c>pageSize</c> and <c>sort</c> are ignored. The export itself is audited.
    /// </summary>
    [HttpGet("export")]
    [RequirePermission(PermissionHelper.Permissions.AuditLogs_View)]
    [Produces("text/csv")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "exportAuditLogs")]
    public async Task<IActionResult> Export([FromQuery] AuditLogFilterQuery filter, [FromQuery] string? q, [FromQuery] string? dir, CancellationToken cancellationToken)
    {
        var parsed = filter.ToFilter(q, ascending: string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase), out var badField);
        if (badField is not null) return ValidationFailure(badField, "Unknown value.");

        var fileName = $"nexpos-audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        // Streamed straight to the client: a 100,000-row export is never held in memory. Once the first bytes are out a failure can only cut the file short.
        await _logs.ExportAsync(parsed, Response.Body, cancellationToken);
        return new EmptyResult();
    }
}

/// <summary>The viewer's filters as they arrive on the query string. Everything is optional.</summary>
public sealed class AuditLogFilterQuery
{
    /// <summary>Entries at or after this instant (ISO-8601; the page converts its local pickers to UTC).</summary>
    [FromQuery(Name = "from")]
    public DateTimeOffset? From { get; set; }

    /// <summary>Entries before this instant (exclusive).</summary>
    [FromQuery(Name = "to")]
    public DateTimeOffset? To { get; set; }

    [FromQuery(Name = "userId")]
    public int? UserId { get; set; }

    /// <summary>For actors that only exist as a name: <c>System</c>, or the name typed into a failed sign-in.</summary>
    [FromQuery(Name = "username")]
    [MaxLength(100)]
    public string? Username { get; set; }

    /// <summary><c>Data</c>, <c>Request</c>, <c>Security</c>, <c>Business</c>, <c>System</c> or <c>Diagnostic</c>.</summary>
    [FromQuery(Name = "category")]
    public string? Category { get; set; }

    /// <summary>The part of the product: <c>Sales</c>, <c>Pos</c>, <c>Inventory</c>, <c>Customers</c>, <c>Account</c>, <c>Settings</c>, <c>Reports</c>, <c>System</c> or <c>Other</c>.</summary>
    [FromQuery(Name = "area")]
    public string? Area { get; set; }

    /// <summary><c>Success</c>, <c>Failure</c> or <c>Denied</c>.</summary>
    [FromQuery(Name = "outcome")]
    public string? Outcome { get; set; }

    /// <summary>Only entries at this severity or worse: <c>Warning</c>, <c>Error</c> or <c>Critical</c>.</summary>
    [FromQuery(Name = "minSeverity")]
    public string? MinSeverity { get; set; }

    [FromQuery(Name = "entityType")]
    [MaxLength(100)]
    public string? EntityType { get; set; }

    [FromQuery(Name = "entityId")]
    public int? EntityId { get; set; }

    [FromQuery(Name = "action")]
    [MaxLength(200)]
    public string? Action { get; set; }

    /// <summary>Everything one HTTP request wrote (the "Request ID").</summary>
    [FromQuery(Name = "correlationId")]
    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    /// <summary>The name of the first filter that is not valid, or null.</summary>
    internal AuditLogFilter ToFilter(string? search, bool ascending, out string? badField)
    {
        string? bad = null;
        T? Parse<T>(string? text, string field) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value)) return value;
            bad ??= field;
            return null;
        }

        var filter = new AuditLogFilter
        {
            FromUtc = From?.UtcDateTime,
            ToUtc = To?.UtcDateTime,
            UserId = UserId,
            Username = Username,
            Category = Parse<AuditCategory>(Category, "category"),
            Area = Parse<AuditArea>(Area, "area"),
            Outcome = Parse<AuditOutcome>(Outcome, "outcome"),
            MinSeverity = Parse<AuditLogSeverity>(MinSeverity, "minSeverity"),
            EntityType = EntityType,
            EntityId = EntityId,
            Action = Action,
            CorrelationId = CorrelationId,
            Search = search,
            Ascending = ascending
        };
        badField = bad;
        return filter;
    }
}
