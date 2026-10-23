using System.Data.Common;
using Core.Auditing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Auditing;

/// <summary>
/// Releases the audit rows of an explicit transaction when it commits and drops them when it rolls
/// back. Without this, a checkout that fails half-way would leave "created order / deducted stock"
/// rows describing things that never happened. (A transaction that is simply disposed after an
/// exception reports nothing; <see cref="AuditChangeTracker.Publish"/> discards those leftovers.)
/// </summary>
public sealed class AuditTransactionInterceptor : DbTransactionInterceptor
{
    private readonly IAuditTrail _trail;
    private readonly AuditChangeTracker _tracker;
    private readonly ILogger<AuditTransactionInterceptor> _logger;

    public AuditTransactionInterceptor(
        IAuditTrail trail, AuditChangeTracker tracker, ILogger<AuditTransactionInterceptor> logger)
    {
        _trail = trail;
        _tracker = tracker;
        _logger = logger;
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => Release(eventData);

    public override Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Release(eventData);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => _tracker.Discard(eventData.Context, eventData.TransactionId);

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _tracker.Discard(eventData.Context, eventData.TransactionId);
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
        => _tracker.Discard(eventData.Context, eventData.TransactionId);

    public override Task TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _tracker.Discard(eventData.Context, eventData.TransactionId);
        return Task.CompletedTask;
    }

    private void Release(TransactionEndEventData eventData)
    {
        try
        {
            _tracker.ReleaseCommitted(eventData.Context, eventData.TransactionId, _trail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit: could not release the rows of a committed transaction");
        }
    }
}
