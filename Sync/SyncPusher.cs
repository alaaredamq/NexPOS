using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using static Sync.SyncSchema;

namespace Sync;

public interface ISyncTransport
{
    Task<SyncAck> PushAsync(SyncBatch batch, CancellationToken ct);
}

/// <summary>The cloud (or the network) said no. <see cref="Retryable"/> tells the pusher whether waiting can help.</summary>
public sealed class SyncTransportException(string code, string message, bool retryable) : Exception(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public sealed class HttpSyncTransport(HttpClient http) : ISyncTransport
{
    public async Task<SyncAck> PushAsync(SyncBatch batch, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("api/sync/push", batch, SyncWire.Json, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SyncTransportException("network", ex.Message, retryable: true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SyncTransportException("timeout", "The cloud did not answer in time.", retryable: true);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<SyncAck>(SyncWire.Json, ct))
                       ?? throw new SyncTransportException("bad_response", "Empty answer from the cloud.", true);

            SyncError? error = null;
            try { error = await response.Content.ReadFromJsonAsync<SyncError>(SyncWire.Json, ct); } catch { /* not JSON */ }
            var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                            || (int)response.StatusCode >= 500;
            // 401/402/409 will not fix themselves in seconds, but the owner can: renew, update, re-enrol. Keep trying slowly.
            var retryable = transient || response.StatusCode is HttpStatusCode.PaymentRequired or HttpStatusCode.Unauthorized or HttpStatusCode.Conflict;
            throw new SyncTransportException(error?.Code ?? $"http_{(int)response.StatusCode}",
                error?.Message ?? $"The cloud answered {(int)response.StatusCode}.", retryable);
        }
    }
}

/// <summary>What the till knows about its own sync, for a status screen or a support call. Thread-safe by lock.</summary>
public sealed class SyncStatus
{
    private readonly object _gate = new();
    private DateTime? _lastAttempt, _lastSuccess;
    private string? _lastError, _lastErrorCode;
    private long _pending;

    public bool Enabled { get; set; }

    public (DateTime? LastAttemptUtc, DateTime? LastSuccessUtc, string? LastError, string? LastErrorCode, long Pending) Snapshot()
    {
        lock (_gate) return (_lastAttempt, _lastSuccess, _lastError, _lastErrorCode, _pending);
    }

    private string? _planStatus;
    private DateTime? _planExpires;

    /// <summary>Subscription state as last reported by the cloud (null until the first successful check).</summary>
    public (string? Status, DateTime? ExpiresAtUtc) Plan()
    {
        lock (_gate) return (_planStatus, _planExpires);
    }

    public void SetPlan(string? status, DateTime? expiresAtUtc)
    {
        lock (_gate) { _planStatus = status; _planExpires = expiresAtUtc; }
    }

    /// <summary>Forgets everything learned about the previous connection (after connecting to, or leaving, a cloud).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _lastAttempt = null; _lastSuccess = null; _lastError = null; _lastErrorCode = null; _pending = 0;
            _planStatus = null; _planExpires = null;
        }
    }

    internal void Attempted(DateTime now) { lock (_gate) _lastAttempt = now; }
    internal void Succeeded(DateTime now, long pending) { lock (_gate) { _lastSuccess = now; _lastError = null; _lastErrorCode = null; _pending = pending; } }
    internal void Failed(string code, string message, long pending) { lock (_gate) { _lastErrorCode = code; _lastError = message; _pending = pending; } }
}

public sealed record SyncRunResult(int BatchesSent, int ChangesSent, long Pending, string? ErrorCode, string? Error)
{
    public bool Ok => ErrorCode is null;
}

/// <summary>
/// Drains the till's change log to the cloud, oldest first. Each batch is acknowledged before the log is
/// trimmed, so a crash or a dropped connection re-sends instead of losing anything; the cloud applies a repeated
/// batch harmlessly because every change is a full-row upsert.
/// </summary>
public sealed class SyncPusher(
    SyncSchema schema,
    string connectionString,
    ISyncTransport transport,
    SyncStatus status,
    TimeProvider time,
    ILogger<SyncPusher>? logger = null,
    int batchSize = 500,
    RowSealer? sealer = null)
{
    public async Task<SyncRunResult> RunOnceAsync(CancellationToken ct)
    {
        status.Attempted(time.GetUtcNow().UtcDateTime);
        int batches = 0, changes = 0;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        try
        {
            SyncTriggers.EnsureTables(connection);
            if (SyncTriggers.GetState(connection, SyncTriggers.SeededKey) != "1")
                SyncTriggers.Seed(connection, schema);

            var schemaVersion = LatestMigration(connection);

            while (!ct.IsCancellationRequested)
            {
                var acked = long.Parse(SyncTriggers.GetState(connection, SyncTriggers.AckedSeqKey) ?? "0");
                var entries = ReadLog(connection, acked, batchSize);
                if (entries.Count == 0) break;

                var batch = BuildBatch(connection, schemaVersion, acked, entries);
                var ack = await transport.PushAsync(batch, ct);

                using (var tx = connection.BeginTransaction())
                {
                    SyncTriggers.SetState(connection, tx, SyncTriggers.AckedSeqKey, batch.ThroughSeq.ToString());
                    using var trim = connection.CreateCommand();
                    trim.Transaction = tx;
                    trim.CommandText = $"DELETE FROM {Q(SyncTriggers.LogTable)} WHERE Seq <= $s";
                    trim.Parameters.AddWithValue("$s", batch.ThroughSeq);
                    trim.ExecuteNonQuery();
                    if (batch.Initial) SyncTriggers.SetState(connection, tx, SyncTriggers.NeedsInitialKey, "0");
                    tx.Commit();
                }

                batches++;
                changes += batch.Changes.Count;

                if (ack.ResyncRequested)
                {
                    logger?.LogInformation("The cloud asked for a full resync; re-queueing every row.");
                    SyncTriggers.Seed(connection, schema);
                }
            }

            var pending = SyncTriggers.PendingCount(connection);
            status.Succeeded(time.GetUtcNow().UtcDateTime, pending);
            return new SyncRunResult(batches, changes, pending, null, null);
        }
        catch (SyncTransportException ex)
        {
            var pending = SafePending(connection);
            logger?.LogWarning("Online sync paused: {Code} {Message}", ex.Code, ex.Message);
            status.Failed(ex.Code, ex.Message, pending);
            return new SyncRunResult(batches, changes, pending, ex.Code, ex.Message);
        }
    }

    private static long SafePending(SqliteConnection connection)
    {
        try { return SyncTriggers.PendingCount(connection); } catch { return -1; }
    }

    private static string LatestMigration(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId DESC LIMIT 1";
        return cmd.ExecuteScalar() as string ?? "";
    }

    private static List<(long Seq, string Table, long RowId)> ReadLog(SqliteConnection connection, long after, int limit)
    {
        var list = new List<(long, string, long)>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT Seq, TableName, RowId FROM {Q(SyncTriggers.LogTable)} WHERE Seq > $a ORDER BY Seq LIMIT $l";
        cmd.Parameters.AddWithValue("$a", after);
        cmd.Parameters.AddWithValue("$l", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)));
        return list;
    }

    /// <summary>Collapses repeated touches of a row into one change that carries the row as it is NOW.</summary>
    private SyncBatch BuildBatch(SqliteConnection connection, string schemaVersion, long after,
        List<(long Seq, string Table, long RowId)> entries)
    {
        var through = entries[^1].Seq;
        var order = new List<(string Table, long RowId)>();
        var seen = new HashSet<(string, long)>();
        foreach (var e in entries)
            if (seen.Add((e.Table, e.RowId))) order.Add((e.Table, e.RowId));

        var changes = new List<SyncChange>(order.Count);
        foreach (var (tableName, rowId) in order)
        {
            if (!schema.TryGetTable(tableName, out var table)) continue; // a table that left the model
            var values = ReadRow(connection, table, rowId);
            changes.Add(values is null ? new SyncChange(tableName, rowId, true, null) : Change(table.Name, rowId, values));
        }

        var initial = SyncTriggers.GetState(connection, SyncTriggers.NeedsInitialKey) == "1";
        return new SyncBatch(schemaVersion, after, through, initial, changes, sealer is null ? SyncFormat.Plain : SyncFormat.Sealed);
    }

    /// <summary>Without a key the row travels as it is. With one, what <see cref="SealPolicy"/> hides is locked first and never sent in the clear.</summary>
    private SyncChange Change(string table, long id, Dictionary<string, object?> values)
    {
        if (sealer is null) return new SyncChange(table, id, false, values);

        var (open, secret) = SealPolicy.Split(table, values);
        return new SyncChange(table, id, false, open, secret is null ? null : sealer.Seal(table, id, secret));
    }

    private static Dictionary<string, object?>? ReadRow(SqliteConnection connection, SyncTable table, long id)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {string.Join(", ", table.Columns.Select(Q))} FROM {Q(table.Name)} WHERE {Q(table.KeyColumn)} = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var row = new Dictionary<string, object?>(table.Columns.Count, StringComparer.Ordinal);
        for (var i = 0; i < table.Columns.Count; i++)
        {
            row[table.Columns[i]] = reader.IsDBNull(i)
                ? null
                : reader.GetValue(i) switch
                {
                    byte[] bytes => new SyncBlob(Convert.ToBase64String(bytes)),
                    var v => v,
                };
        }
        return row;
    }
}
