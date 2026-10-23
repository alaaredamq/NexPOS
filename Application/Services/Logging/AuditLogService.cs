using System.Text.Json;
using Application.DTOs.Logging;
using Application.IServices.Logging;
using Core.Auditing;
using Core.Entities.Logging;
using Core.IRepositories.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Services.Logging;

public class AuditLogService : IAuditLogService
{
    private static readonly TimeSpan FilterOptionsTtl = TimeSpan.FromSeconds(60);
    private const string FilterOptionsCacheKey = "audit:filter-options";

    private readonly IAuditLogRepository _repository;
    private readonly IMemoryCache _cache;

    public AuditLogService(IAuditLogRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public Task<(List<AuditLog> Items, int Total)> SearchAsync(
        AuditLogFilter filter, int skip, int take, CancellationToken cancellationToken = default)
        => _repository.SearchAsync(filter, Math.Max(0, skip), Math.Clamp(take, 1, 500), cancellationToken);

    public async Task<AuditLogDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await _repository.GetByIdAsync(id, cancellationToken);
        if (entry is null) return null;

        var related = 1;
        if (!string.IsNullOrEmpty(entry.CorrelationId))
        {
            var (_, total) = await _repository.SearchAsync(
                new AuditLogFilter { CorrelationId = entry.CorrelationId }, 0, 1, cancellationToken);
            related = total;
        }

        return new AuditLogDetails
        {
            Entry = entry,
            Changes = BuildChanges(entry),
            RowsInSameRequest = related
        };
    }

    public Task<AuditLogStats> GetStatsAsync(CancellationToken cancellationToken = default)
        => _repository.GetStatsAsync(DateTime.UtcNow, cancellationToken);

    public async Task<AuditLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(FilterOptionsCacheKey, out AuditLogFilterOptions? cached) && cached is not null)
            return cached;

        var options = await _repository.GetFilterOptionsAsync(cancellationToken);
        _cache.Set(FilterOptionsCacheKey, options, FilterOptionsTtl);
        return options;
    }

    public Task<int> ExportCsvAsync(AuditLogFilter filter, Stream output, CancellationToken cancellationToken = default)
        => AuditLogCsv.WriteAsync(
            _repository.StreamAsync(filter, IAuditLogService.ExportMaxRows, cancellationToken), output, cancellationToken);

    // ---- old -> new ---------------------------------------------------------------------------------------------

    internal static List<AuditFieldChange> BuildChanges(AuditLog entry)
    {
        var before = Flatten(entry.OldValuesJson);
        var after = Flatten(entry.NewValuesJson);
        if (before.Count == 0 && after.Count == 0) return [];

        // Keep the order the fields were recorded in (the changed fields first, as they were detected).
        var fields = before.Keys.Concat(after.Keys).Distinct().ToList();
        return fields
            .Select(field => new AuditFieldChange(
                field,
                before.GetValueOrDefault(field),
                after.GetValueOrDefault(field)))
            .ToList();
    }

    private static Dictionary<string, string?> Flatten(string? json)
    {
        var result = new Dictionary<string, string?>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }
        }
        catch (JsonException)
        {
            // A damaged JSON column must not break the detail view; the raw text is still on the entry.
        }

        return result;
    }
}
