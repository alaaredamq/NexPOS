using Application.DTOs.Logging;
using Application.IServices.Base;
using Core.Auditing;
using Core.Entities.Logging;

namespace Application.IServices.Logging;

/// <summary>
/// Reading the audit trail (viewer, details, export). Writing happens through
/// <see cref="IAuditTrail"/>; there is deliberately no way to edit or delete an entry from here.
/// </summary>
public interface IAuditLogService : IBaseService
{
    /// <summary>Largest number of rows one export will contain.</summary>
    const int ExportMaxRows = 100_000;

    Task<(List<AuditLog> Items, int Total)> SearchAsync(
        AuditLogFilter filter, int skip, int take, CancellationToken cancellationToken = default);

    Task<AuditLogDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken = default);

    Task<AuditLogStats> GetStatsAsync(CancellationToken cancellationToken = default);

    Task<AuditLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the matching rows (newest first, capped at <see cref="ExportMaxRows"/>) as CSV.</summary>
    Task<int> ExportCsvAsync(AuditLogFilter filter, Stream output, CancellationToken cancellationToken = default);
}
