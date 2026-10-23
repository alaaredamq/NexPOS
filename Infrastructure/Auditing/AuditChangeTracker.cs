using System.Runtime.CompilerServices;
using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Auditing;

/// <summary>
/// Remembers, per DbContext instance, which changes are being saved right now and which finished
/// rows are waiting for their transaction to commit. Shared by the three audit interceptors.
/// </summary>
public sealed class AuditChangeTracker
{
    private readonly ConditionalWeakTable<DbContext, ContextState> _states = new();

    internal ContextState For(DbContext context) => _states.GetValue(context, static _ => new ContextState());

    internal bool TryGet(DbContext context, out ContextState state) => _states.TryGetValue(context, out state!);

    internal sealed class ContextState
    {
        /// <summary>Changes captured in SavingChanges, completed in SavedChanges.</summary>
        public List<AuditEntityDescriber.PendingChange> Pending { get; } = [];

        /// <summary>Finished rows from inside an explicit transaction, tagged with that transaction's id.</summary>
        public List<(Guid TransactionId, AuditLog Row)> AwaitingCommit { get; } = [];

        /// <summary>True while EF itself is writing changes, so the command interceptor ignores its statements.</summary>
        public bool InSave { get; set; }
    }

    /// <summary>
    /// Hands rows to the trail now - or, inside an explicit transaction (the POS checkout), holds them
    /// until that transaction commits, so a sale that rolls back leaves no "this happened" rows behind.
    /// </summary>
    internal void Publish(DbContext context, IAuditTrail trail, IEnumerable<AuditLog> rows)
    {
        var transaction = context.Database.CurrentTransaction;
        var state = For(context);

        if (transaction is null)
        {
            // No transaction is open, so anything still held belongs to one that ended without a commit.
            state.AwaitingCommit.Clear();
            foreach (var row in rows) trail.Record(row);
            return;
        }

        // EF has no "disposed" hook: a transaction abandoned by an exception never reports its rollback.
        // Rows tagged with any other transaction id are such leftovers - drop them.
        state.AwaitingCommit.RemoveAll(held => held.TransactionId != transaction.TransactionId);
        state.AwaitingCommit.AddRange(rows.Select(row => (transaction.TransactionId, row)));
    }

    internal void ReleaseCommitted(DbContext? context, Guid transactionId, IAuditTrail trail)
    {
        if (context is null || !TryGet(context, out var state)) return;

        var committed = state.AwaitingCommit.Where(held => held.TransactionId == transactionId).Select(held => held.Row).ToList();
        state.AwaitingCommit.RemoveAll(held => held.TransactionId == transactionId);

        foreach (var row in committed) trail.Record(row);
    }

    internal void Discard(DbContext? context, Guid transactionId)
    {
        if (context is not null && TryGet(context, out var state))
        {
            state.AwaitingCommit.RemoveAll(held => held.TransactionId == transactionId);
        }
    }
}
