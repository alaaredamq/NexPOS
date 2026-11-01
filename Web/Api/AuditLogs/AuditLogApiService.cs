using System.Text.Json;
using Application.IServices.Logging;
using Core.Auditing;
using Core.Entities.Logging;
using Web.Api.AuditLogs.Contracts;
using Web.Api.Infrastructure;

namespace Web.Api.AuditLogs;

public interface IAuditLogApiService
{
    /// <summary>One page of matching entries, in the order the filter asks for (<see cref="AuditLogFilter.Ascending"/>).</summary>
    Task<PagedResult<AuditLogDto>> ListAsync(AuditLogFilter filter, PagedQuery query, CancellationToken cancellationToken);

    /// <summary>Null when the entry does not exist.</summary>
    Task<AuditLogDetailsDto?> GetAsync(int id, CancellationToken cancellationToken);

    Task<AuditLogStatsDto> StatsAsync(CancellationToken cancellationToken);

    Task<AuditLogOptionsDto> OptionsAsync(CancellationToken cancellationToken);

    /// <summary>Writes the matching rows as CSV (up to <see cref="IAuditLogService.ExportMaxRows"/>) and records who pulled them. Returns the row count.</summary>
    Task<int> ExportAsync(AuditLogFilter filter, Stream output, CancellationToken cancellationToken);
}

/// <summary>Maps the shared, read-only <see cref="IAuditLogService"/> to the API's DTOs. There is deliberately no way to change an entry.</summary>
public sealed class AuditLogApiService : IAuditLogApiService
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private readonly IAuditLogService _logs;
    private readonly IAuditTrail _audit;

    public AuditLogApiService(IAuditLogService logs, IAuditTrail audit)
    {
        _logs = logs;
        _audit = audit;
    }

    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogFilter filter, PagedQuery query, CancellationToken cancellationToken)
    {
        var (items, total) = await _logs.SearchAsync(filter, query.Skip, query.PageSize, cancellationToken);
        return PagedResult<AuditLogDto>.Of(items.Select(ToDto).ToList(), total, query);
    }

    public async Task<AuditLogDetailsDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var details = await _logs.GetDetailsAsync(id, cancellationToken);
        if (details is null) return null;

        var e = details.Entry;
        return new AuditLogDetailsDto
        {
            Entry = ToDto(e),
            Changes = details.Changes.Select(c => new AuditFieldChangeDto { Field = c.Field, OldValue = c.Old, NewValue = c.New }).ToList(),
            Additional = PrettyJson(e.AdditionalInfoJson),
            IpAddress = NullIfEmpty(e.IpAddress),
            UserAgent = NullIfEmpty(e.UserAgent),
            Machine = e.MachineName,
            AppVersion = e.AppVersion,
            CorrelationId = e.CorrelationId,
            Source = e.Source,
            HttpMethod = e.HttpMethod,
            Path = e.RequestPath,
            RowsInSameRequest = details.RowsInSameRequest
        };
    }

    public async Task<AuditLogStatsDto> StatsAsync(CancellationToken cancellationToken)
    {
        var stats = await _logs.GetStatsAsync(cancellationToken);
        return new AuditLogStatsDto
        {
            Total = stats.Total,
            Last24Hours = stats.Last24Hours,
            Failures24Hours = stats.Failures24Hours,
            Security24Hours = stats.Security24Hours,
            OldestUtc = stats.OldestUtc is { } oldest ? AsUtc(oldest) : null
        };
    }

    public async Task<AuditLogOptionsDto> OptionsAsync(CancellationToken cancellationToken)
    {
        var options = await _logs.GetFilterOptionsAsync(cancellationToken);
        return new AuditLogOptionsDto
        {
            Users = options.Users.Select(u => new AuditUserOptionDto { UserId = u.UserId, Username = u.Username }).ToList(),
            EntityTypes = options.EntityTypes,
            Actions = options.Actions
        };
    }

    public async Task<int> ExportAsync(AuditLogFilter filter, Stream output, CancellationToken cancellationToken)
    {
        var rows = await _logs.ExportCsvAsync(filter, output, cancellationToken);

        // Who pulled the history out of the system is itself worth remembering (same event as the Razor export).
        _audit.LogBusiness(AuditActions.AuditExported, $"Audit log exported to CSV ({rows} rows)", "Audit",
            details: new { rows, filter });
        return rows;
    }

    private static AuditLogDto ToDto(AuditLog e) => new()
    {
        Id = e.Id,
        Time = AsUtc(e.CreatedAt),
        UserId = e.UserId,
        User = e.Username,
        Role = e.UserRole,
        Category = e.Category.ToString(),
        Action = e.Action,
        EntityType = e.EntityType,
        EntityId = e.EntityId,
        Label = e.EntityLabel,
        Description = e.Description,
        Outcome = e.Outcome.ToString(),
        Severity = e.Severity.ToString(),
        StatusCode = e.StatusCode,
        DurationMs = e.DurationMs
    };

    // The audit database stores UTC instants without a kind; say so, or the JSON would carry no "Z".
    private static DateTime AsUtc(DateTime stored) => DateTime.SpecifyKind(stored, DateTimeKind.Utc);

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

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
