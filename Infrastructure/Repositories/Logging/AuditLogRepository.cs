using System.Runtime.CompilerServices;
using Core.Auditing;
using Core.Entities.Logging;
using Core.IRepositories.Logging;
using Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Logging;

/// <summary>
/// Read access to the audit trail (plus the opt-in retention purge). Not a <c>BaseRepository</c>:
/// the audit store is append-only, so it intentionally offers no save/update/delete - entries are
/// written by <see cref="IAuditTrail"/>.
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly AuditDbContext _db;

    public AuditLogRepository(AuditDbContext db)
    {
        _db = db;
    }

    private IQueryable<AuditLog> Rows => _db.AuditLogs.AsNoTracking();

    public Task<AuditLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => Rows.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<(List<AuditLog> Items, int Total)> SearchAsync(
        AuditLogFilter filter, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = Apply(Rows, filter);
        var total = await query.CountAsync(cancellationToken);
        var items = await Order(query, filter).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async IAsyncEnumerable<AuditLog> StreamAsync(
        AuditLogFilter filter, int maxRows, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = Order(Apply(Rows, filter), filter).Take(maxRows);
        await foreach (var row in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return row;
        }
    }

    public async Task<AuditLogStats> GetStatsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var since = nowUtc.AddHours(-24);

        return new AuditLogStats
        {
            Total = await Rows.CountAsync(cancellationToken),
            Last24Hours = await Rows.CountAsync(x => x.CreatedAt >= since, cancellationToken),
            Failures24Hours = await Rows.CountAsync(
                x => x.CreatedAt >= since && x.Outcome != AuditOutcome.Success, cancellationToken),
            Security24Hours = await Rows.CountAsync(
                x => x.CreatedAt >= since && x.Category == AuditCategory.Security, cancellationToken),
            OldestUtc = await Rows.OrderBy(x => x.CreatedAt)
                .Select(x => (DateTime?)x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
        };
    }

    public async Task<AuditLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        var users = await Rows
            .Where(x => x.Username != null)
            .Select(x => new { x.UserId, Username = x.Username! })
            .Distinct()
            .OrderBy(x => x.Username)
            .Take(300)
            .ToListAsync(cancellationToken);

        var entityTypes = await Rows
            .Select(x => x.EntityType)
            .Distinct()
            .OrderBy(x => x)
            .Take(300)
            .ToListAsync(cancellationToken);

        // Request rows carry one action per route ("GET Products/Index", ...) - far too many for a
        // drop-down; they are findable through the free-text search instead.
        var actions = await Rows
            .Where(x => x.Category != AuditCategory.Request)
            .Select(x => x.Action)
            .Distinct()
            .OrderBy(x => x)
            .Take(300)
            .ToListAsync(cancellationToken);

        return new AuditLogFilterOptions
        {
            Users = users.Select(x => new AuditUserOption(x.UserId, x.Username)).ToList(),
            EntityTypes = entityTypes,
            Actions = actions
        };
    }

    public Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
        => _db.AuditLogs.Where(x => x.CreatedAt < cutoffUtc).ExecuteDeleteAsync(cancellationToken);

    // ---- query building -----------------------------------------------------------------------------------------

    internal static IQueryable<AuditLog> Apply(IQueryable<AuditLog> query, AuditLogFilter filter)
    {
        if (filter.FromUtc is { } from) query = query.Where(x => x.CreatedAt >= from);
        if (filter.ToUtc is { } to) query = query.Where(x => x.CreatedAt < to);
        if (filter.UserId is { } userId) query = query.Where(x => x.UserId == userId);
        if (filter.Category is { } category) query = query.Where(x => x.Category == category);
        if (filter.Outcome is { } outcome) query = query.Where(x => x.Outcome == outcome);
        if (filter.EntityId is { } entityId) query = query.Where(x => x.EntityId == entityId);

        if (filter.MinSeverity is { } min)
        {
            var allowed = Enum.GetValues<AuditLogSeverity>().Where(s => s >= min).ToArray();
            query = query.Where(x => allowed.Contains(x.Severity));
        }

        // These columns are NOCASE, so plain equality is case-insensitive.
        if (!string.IsNullOrWhiteSpace(filter.Username))
        {
            var username = filter.Username.Trim();
            query = query.Where(x => x.Username == username);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            var entityType = filter.EntityType.Trim();
            query = query.Where(x => x.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(x => x.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            var correlationId = filter.CorrelationId.Trim();
            query = query.Where(x => x.CorrelationId == correlationId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + EscapeLike(filter.Search.Trim()) + "%";
            query = query.Where(x =>
                EF.Functions.Like(x.Description!, pattern, "\\") ||
                EF.Functions.Like(x.EntityLabel!, pattern, "\\") ||
                EF.Functions.Like(x.Username!, pattern, "\\") ||
                EF.Functions.Like(x.Action, pattern, "\\") ||
                EF.Functions.Like(x.EntityType, pattern, "\\") ||
                EF.Functions.Like(x.RequestPath!, pattern, "\\") ||
                EF.Functions.Like(x.IpAddress, pattern, "\\") ||
                EF.Functions.Like(x.CorrelationId!, pattern, "\\") ||
                EF.Functions.Like(x.ChangedFields!, pattern, "\\") ||
                EF.Functions.Like(x.MachineName!, pattern, "\\"));
        }

        return query;
    }

    private static IOrderedQueryable<AuditLog> Order(IQueryable<AuditLog> query, AuditLogFilter filter)
        => filter.Ascending
            ? query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            : query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);

    private static string EscapeLike(string text)
        => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
