using Core.Auditing;
using Core.Entities.Logging;

namespace Core.IRepositories.Logging;

/// <summary>
/// Read side of the audit trail. Deliberately not an <c>IBaseRepository</c>: audit rows are
/// append-only, so there is no save/update/delete here - rows are written by <see cref="IAuditTrail"/>,
/// and the only removal is the opt-in retention purge.
/// </summary>
public interface IAuditLogRepository
{
    Task<AuditLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<(List<AuditLog> Items, int Total)> SearchAsync(
        AuditLogFilter filter, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Streams matching rows (for export) without loading them all into memory.</summary>
    IAsyncEnumerable<AuditLog> StreamAsync(
        AuditLogFilter filter, int maxRows, CancellationToken cancellationToken = default);

    Task<AuditLogStats> GetStatsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<AuditLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes rows created before <paramref name="cutoffUtc"/>; returns how many.</summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
