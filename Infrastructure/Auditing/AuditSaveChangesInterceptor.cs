using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Auditing;

/// <summary>
/// Records every record that is created, changed or deleted through EF Core, wherever in the
/// application the change comes from - so a feature added tomorrow is audited without anybody having
/// to remember to log it. Auditing problems are logged and swallowed: they must never fail a save.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IAuditTrail _trail;
    private readonly IAuditContext _context;
    private readonly AuditChangeTracker _tracker;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;

    public AuditSaveChangesInterceptor(
        IAuditTrail trail, IAuditContext context, AuditChangeTracker tracker, ILogger<AuditSaveChangesInterceptor> logger)
    {
        _trail = trail;
        _context = context;
        _tracker = tracker;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Complete(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Complete(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
        => Fail(eventData.Context, eventData.Exception);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Fail(eventData.Context, eventData.Exception);
        return Task.CompletedTask;
    }

    // A cancelled save never reaches SavedChanges/SaveChangesFailed; without this the "EF is writing"
    // flag would stay set and blind the bulk-write safety net for the rest of the context's life.
    public override void SaveChangesCanceled(DbContextEventData eventData) => Reset(eventData.Context);

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Reset(eventData.Context);
        return Task.CompletedTask;
    }

    private void Reset(DbContext? context)
    {
        if (context is null || !_tracker.TryGet(context, out var state)) return;
        state.InSave = false;
        state.Pending.Clear();
    }

    private void Capture(DbContext? context)
    {
        if (context is null) return;

        try
        {
            var state = _tracker.For(context);
            state.Pending.Clear();

            if (_context.DataChangesSuppressed) return;

            // SaveChanges runs DetectChanges *after* this hook, so run it here to see edits made to tracked entities.
            context.ChangeTracker.DetectChanges();

            foreach (var entry in context.ChangeTracker.Entries().ToList())
            {
                var change = AuditEntityDescriber.Inspect(entry);
                if (change is not null) state.Pending.Add(change);
            }

            state.InSave = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit: could not inspect pending changes; this save will not be audited");
        }
    }

    private void Complete(DbContext? context)
    {
        if (context is null || !_tracker.TryGet(context, out var state)) return;

        try
        {
            state.InSave = false;
            if (state.Pending.Count == 0) return;

            var rows = state.Pending.Select(AuditEntityDescriber.ToAuditLog).ToList();
            state.Pending.Clear();
            _tracker.Publish(context, _trail, rows);
        }
        catch (Exception ex)
        {
            state.Pending.Clear();
            _logger.LogWarning(ex, "Audit: could not record saved changes");
        }
    }

    private void Fail(DbContext? context, Exception exception)
    {
        if (context is null || !_tracker.TryGet(context, out var state)) return;

        try
        {
            state.InSave = false;
            var attempted = state.Pending.ToList();
            state.Pending.Clear();
            if (attempted.Count == 0) return;

            // The save did not happen, so there are no "data" rows - but the attempt and the reason are
            // exactly what support needs when someone says "it wouldn't let me save".
            var summary = attempted
                .GroupBy(c => $"{c.Action} {c.EntityType}")
                .Select(g => $"{g.Key} x{g.Count()}");

            _trail.Record(new AuditLog
            {
                Category = AuditCategory.Data,
                Action = AuditActions.SaveFailed,
                EntityType = attempted.Count == 1 || attempted.Select(c => c.EntityType).Distinct().Count() == 1
                    ? attempted[0].EntityType
                    : "Multiple",
                EntityId = attempted.Count == 1 ? attempted[0].EntityId : null,
                EntityLabel = attempted.Count == 1 ? attempted[0].Label : null,
                Description = AuditRedactor.Truncate(
                    $"Save failed ({string.Join(", ", summary)}): {Root(exception).Message}", 1000),
                Outcome = AuditOutcome.Failure,
                Severity = AuditLogSeverity.Error,
                AdditionalInfoJson = AuditRedactor.SerializeDetails(new
                {
                    attempted = attempted.Select(c => new { c.Action, c.EntityType, c.EntityId, c.Label }),
                    exception = new { type = Root(exception).GetType().Name, message = Root(exception).Message }
                })
            });
        }
        catch (Exception ex)
        {
            state.Pending.Clear();
            _logger.LogWarning(ex, "Audit: could not record a failed save");
        }
    }

    private static Exception Root(Exception exception)
    {
        while (exception.InnerException is not null) exception = exception.InnerException;
        return exception;
    }
}
