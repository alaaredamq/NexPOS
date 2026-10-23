using System.Globalization;
using System.Text.Json;
using Application.IServices.Logging;
using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

/// <summary>
/// The audit trail viewer for support and maintenance: search and filter everything that happened,
/// open an entry to see exactly what changed, follow a request or a record through the log, and
/// export the lot to CSV. Strictly read-only - there is no way to add, edit or delete an entry
/// from here (or from anywhere else in the application).
/// </summary>
[Authorize]
[RequirePermission(PermissionHelper.Permissions.AuditLogs_View)]
public class AuditLogsController : BaseController
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private readonly IAuditLogService _service;
    private readonly IAuditTrail _auditTrail;
    private readonly ILogger<AuditLogsController> _logger;

    public AuditLogsController(IAuditLogService service, IAuditTrail auditTrail, ILogger<AuditLogsController> logger)
    {
        _service = service;
        _auditTrail = auditTrail;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewBag.Options = await _service.GetFilterOptionsAsync(cancellationToken);
        ViewBag.Stats = await _service.GetStatsAsync(cancellationToken);
        return View();
    }

    /// <summary>One page of matching entries, in the shape DataTables' server-side mode expects.</summary>
    [HttpGet]
    public async Task<IActionResult> GetData(
        [FromQuery] AuditLogQuery query, int draw = 0, int start = 0, int length = 25, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = query.ToFilter();
            var (items, total) = await _service.SearchAsync(filter, start, length, cancellationToken);

            return Json(new
            {
                draw,
                recordsTotal = total,
                recordsFiltered = total,
                data = items.Select(x => new
                {
                    id = x.Id,
                    time = AsUtc(x.CreatedAt),
                    user = x.Username,
                    role = x.UserRole,
                    category = x.Category.ToString(),
                    action = x.Action,
                    entityType = x.EntityType,
                    entityId = x.EntityId,
                    label = x.EntityLabel,
                    description = x.Description,
                    outcome = x.Outcome.ToString(),
                    severity = x.Severity.ToString(),
                    status = x.StatusCode,
                    durationMs = x.DurationMs
                })
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The audit log could not be read");
            return Json(new { draw, error = "The audit log could not be read." });
        }
    }

    /// <summary>Everything about one entry: who, what, where, the field-by-field changes and the raw details.</summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var details = await _service.GetDetailsAsync(id, cancellationToken);
        if (details is null) return NotFound(new { success = false, message = "Entry not found" });

        var e = details.Entry;
        return Json(new
        {
            success = true,
            id = e.Id,
            time = AsUtc(e.CreatedAt),
            userId = e.UserId,
            user = e.Username,
            role = e.UserRole,
            category = e.Category.ToString(),
            action = e.Action,
            entityType = e.EntityType,
            entityId = e.EntityId,
            label = e.EntityLabel,
            description = e.Description,
            outcome = e.Outcome.ToString(),
            severity = e.Severity.ToString(),
            changedFields = e.ChangedFields,
            changes = details.Changes.Select(c => new { field = c.Field, oldValue = c.Old, newValue = c.New }),
            additional = PrettyJson(e.AdditionalInfoJson),
            ip = e.IpAddress,
            userAgent = e.UserAgent,
            machine = e.MachineName,
            version = e.AppVersion,
            correlationId = e.CorrelationId,
            source = e.Source,
            method = e.HttpMethod,
            path = e.RequestPath,
            status = e.StatusCode,
            durationMs = e.DurationMs,
            rowsInSameRequest = details.RowsInSameRequest
        });
    }

    /// <summary>The current filter as a CSV download (newest first, up to 100,000 rows), streamed as it is read.</summary>
    [HttpGet]
    public async Task Export([FromQuery] AuditLogQuery query, CancellationToken cancellationToken)
    {
        var filter = query.ToFilter();
        var fileName = $"nexpos-audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv";

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        var rows = await _service.ExportCsvAsync(filter, Response.Body, cancellationToken);

        // Who pulled the history out of the system is itself worth remembering.
        _auditTrail.LogBusiness(AuditActions.AuditExported, $"Audit log exported to CSV ({rows} rows)", "Audit",
            details: new { rows, fileName, filter = query });
    }

    private static DateTime AsUtc(DateTime stored) => DateTime.SpecifyKind(stored, DateTimeKind.Utc);

    private static string? PrettyJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json), Pretty);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}

/// <summary>The viewer's filters as they arrive on the query string.</summary>
public sealed class AuditLogQuery
{
    /// <summary>ISO-8601 instants; the page converts its local date/time pickers to UTC before sending.</summary>
    public string? From { get; set; }
    public string? To { get; set; }
    public int? UserId { get; set; }

    /// <summary>For actors that only exist as a name: "System", or the name typed into a failed sign-in.</summary>
    public string? Username { get; set; }
    public string? Category { get; set; }
    public string? Outcome { get; set; }
    public string? MinSeverity { get; set; }
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? Action { get; set; }
    public string? CorrelationId { get; set; }
    public string? Search { get; set; }
    public bool Ascending { get; set; }

    public AuditLogFilter ToFilter() => new()
    {
        FromUtc = ParseUtc(From),
        ToUtc = ParseUtc(To),
        UserId = UserId,
        Username = Username,
        Category = ParseEnum<AuditCategory>(Category),
        Outcome = ParseEnum<AuditOutcome>(Outcome),
        MinSeverity = ParseEnum<AuditLogSeverity>(MinSeverity),
        EntityType = EntityType,
        EntityId = EntityId,
        Action = Action,
        CorrelationId = CorrelationId,
        Search = Search,
        Ascending = Ascending
    };

    private static DateTime? ParseUtc(string? text)
        => DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;

    private static T? ParseEnum<T>(string? text) where T : struct, Enum
        => Enum.TryParse<T>(text, ignoreCase: true, out var value) ? value : null;
}
