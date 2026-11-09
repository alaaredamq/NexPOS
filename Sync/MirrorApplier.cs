using Microsoft.Data.Sqlite;
using static Sync.SyncSchema;

namespace Sync;

/// <summary>
/// Writes a till's batch into that site's mirror database (same schema as the till, one file per site).
/// <para>
/// Everything is checked against <see cref="SyncSchema"/>: only whitelisted tables and columns are written, so a
/// forged batch cannot reach another table, and nothing in a batch is ever spliced into SQL as text except
/// identifiers that came from that whitelist. A batch is applied in one transaction: all of it or none of it.
/// </para>
/// </summary>
public sealed class MirrorApplier(SyncSchema schema)
{
    private sealed record ColumnInfo(string Name, bool NotNull, bool HasDefault, bool IsKey, string Type);

    /// <summary>Where sealed rows live: one table beside the shop's own schema, made by the cloud (never by the till's migrations).</summary>
    public const string SealedTable = "SealedRows";

    /// <summary>Idempotent. Called when a mirror is created and before every batch, so older mirrors gain it too.</summary>
    public static void EnsureSealedTable(SqliteConnection connection, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {Q(SealedTable)} (TableName TEXT NOT NULL, Id INTEGER NOT NULL, KeyId TEXT NOT NULL, Blob TEXT NOT NULL, PRIMARY KEY (TableName, Id)) WITHOUT ROWID;";
        cmd.ExecuteNonQuery();
    }

    /// <returns>Number of changes applied.</returns>
    public int Apply(string mirrorPath, SyncBatch batch)
    {
        if (batch.Changes.Count > 0 && batch.Changes.Any(c => !schema.TryGetTable(c.Table, out _)))
        {
            var unknown = batch.Changes.First(c => !schema.TryGetTable(c.Table, out _)).Table;
            throw new SyncBatchException($"Unknown table '{unknown}'.");
        }

        Validate(batch);

        // Foreign keys off: a batch lists parents and children in log order, not dependency order, and the
        // till already enforced integrity when it wrote the data. Cascades must not run either: the till logs
        // every cascaded row itself.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = mirrorPath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        // Deleted rows are overwritten with zeros instead of lingering in free pages: a shop that switches to sealed has its old
        // readable copy wiped by the initial batch, and that wipe has to actually remove the text.
        Exec(connection, null, "PRAGMA secure_delete = ON;");
        using var tx = connection.BeginTransaction();
        EnsureSealedTable(connection, tx);

        if (batch.Initial)
        {
            foreach (var table in schema.Tables)
                Exec(connection, tx, $"DELETE FROM {Q(table.Name)};");
            Exec(connection, tx, $"DELETE FROM {Q(SealedTable)};");
        }

        var columnCache = new Dictionary<string, IReadOnlyList<ColumnInfo>>(StringComparer.Ordinal);
        var applied = 0;
        foreach (var change in batch.Changes)
        {
            var table = schema.TryGetTable(change.Table, out var t) ? t : throw new SyncBatchException($"Unknown table '{change.Table}'.");

            if (change.Deleted)
            {
                using var del = connection.CreateCommand();
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {Q(table.Name)} WHERE {Q(table.KeyColumn)} = $id";
                del.Parameters.AddWithValue("$id", change.Id);
                del.ExecuteNonQuery();
                DeleteSealed(connection, tx, table.Name, change.Id);
            }
            else
            {
                if (change.Values is null && change.Sealed is null)
                    throw new SyncBatchException($"Change for {change.Table}#{change.Id} has no values.");
                if (change.Values is not null)
                {
                    if (!columnCache.TryGetValue(table.Name, out var actual))
                        columnCache[table.Name] = actual = ReadColumns(connection, tx, table.Name);
                    Upsert(connection, tx, table, actual, change);
                }
                if (change.Sealed is not null) UpsertSealed(connection, tx, table.Name, change);
            }
            applied++;
        }

        tx.Commit();

        // The write-ahead log still holds the pages as they were before the wipe. Fold them into the database file and empty the
        // log now (best effort: a reader holding the log open just means the next checkpoint does it).
        if (batch.Initial) TryCheckpoint(connection);
        return applied;
    }

    private static void TryCheckpoint(SqliteConnection connection)
    {
        try { Exec(connection, null, "PRAGMA wal_checkpoint(TRUNCATE);"); }
        catch (SqliteException) { /* busy: the automatic checkpoint will get to it */ }
    }

    /// <summary>
    /// A sealed batch must not carry anything in the clear that <see cref="SealPolicy"/> hides, whatever the till says:
    /// a bug (or a downgraded till) cannot leak a sealed table through a batch that claims to be sealed. The cloud checks
    /// shapes only; it has no key and cannot tell good ciphertext from bad.
    /// </summary>
    private static void Validate(SyncBatch batch)
    {
        if (batch.Format is not (SyncFormat.Plain or SyncFormat.Sealed))
            throw new SyncBatchException($"Unknown sync format {batch.Format}. Upgrade the cloud.");

        foreach (var change in batch.Changes)
        {
            if (change.Sealed is not null && !RowSealer.TryReadKeyId(change.Sealed, out _))
                throw new SyncBatchException($"Change for {change.Table}#{change.Id} carries a malformed sealed value.");

            if (batch.Format == SyncFormat.Plain)
            {
                if (change.Sealed is not null)
                    throw new SyncBatchException($"Change for {change.Table}#{change.Id} is sealed but the batch says it is not.");
                continue;
            }

            if (change.Deleted) continue;
            if (SealPolicy.HasSealedPart(change.Table) && change.Sealed is null)
                throw new SyncBatchException($"Change for {change.Table}#{change.Id} has a sealed part that was not sealed.");
            if (SealPolicy.IsFullySealed(change.Table) && change.Values is not null)
                throw new SyncBatchException($"Change for {change.Table}#{change.Id} sends a sealed table in the clear.");
            if (change.Values is not null && SealPolicy.SealedColumnsOf(change.Table).Any(change.Values.ContainsKey))
                throw new SyncBatchException($"Change for {change.Table}#{change.Id} sends a sealed column in the clear.");
        }
    }

    private static void UpsertSealed(SqliteConnection connection, SqliteTransaction tx, string table, SyncChange change)
    {
        RowSealer.TryReadKeyId(change.Sealed!, out var keyId); // shape already checked in Validate
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {Q(SealedTable)} (TableName, Id, KeyId, Blob) VALUES ($t, $id, $k, $b) " +
                          "ON CONFLICT(TableName, Id) DO UPDATE SET KeyId = excluded.KeyId, Blob = excluded.Blob";
        cmd.Parameters.AddWithValue("$t", table);
        cmd.Parameters.AddWithValue("$id", change.Id);
        cmd.Parameters.AddWithValue("$k", keyId);
        cmd.Parameters.AddWithValue("$b", change.Sealed);
        cmd.ExecuteNonQuery();
    }

    private static void DeleteSealed(SqliteConnection connection, SqliteTransaction tx, string table, long id)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"DELETE FROM {Q(SealedTable)} WHERE TableName = $t AND Id = $id";
        cmd.Parameters.AddWithValue("$t", table);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static void Upsert(SqliteConnection connection, SqliteTransaction tx, SyncTable table,
        IReadOnlyList<ColumnInfo> actual, SyncChange change)
    {
        var allowed = new HashSet<string>(table.Columns, StringComparer.Ordinal);
        var actualNames = actual.ToDictionary(c => c.Name, StringComparer.Ordinal);

        var columns = new List<string>();
        var parameters = new List<object>();
        foreach (var (name, value) in change.Values!)
        {
            // Unknown here = a column added on a newer till than this cloud's schema knows; skip it rather than fail.
            if (!allowed.Contains(name) || !actualNames.ContainsKey(name) || name == table.KeyColumn) continue;
            columns.Add(name);
            parameters.Add(SyncWire.ToDbValue(value));
        }

        columns.Insert(0, table.KeyColumn);
        parameters.Insert(0, change.Id);

        // Required columns the till never sends (credentials) still need a value on first insert.
        var insertColumns = new List<string>(columns);
        var insertParameters = new List<object>(parameters);
        foreach (var column in actual)
        {
            if (!column.NotNull || column.HasDefault || column.IsKey || columns.Contains(column.Name)) continue;
            insertColumns.Add(column.Name);
            insertParameters.Add(IsTextual(column.Type) ? "" : 0L);
        }

        var updateColumns = columns.Where(c => c != table.KeyColumn).ToList();
        var sql = new System.Text.StringBuilder();
        sql.Append($"INSERT INTO {Q(table.Name)} ({string.Join(", ", insertColumns.Select(Q))}) VALUES ({string.Join(", ", insertColumns.Select((_, i) => "$p" + i))})");
        sql.Append($" ON CONFLICT({Q(table.KeyColumn)}) ");
        sql.Append(updateColumns.Count == 0
            ? "DO NOTHING"
            : "DO UPDATE SET " + string.Join(", ", updateColumns.Select(c => $"{Q(c)} = excluded.{Q(c)}")));

        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql.ToString();
        for (var i = 0; i < insertParameters.Count; i++)
            cmd.Parameters.AddWithValue("$p" + i, insertParameters[i]);
        cmd.ExecuteNonQuery();
    }

    private static bool IsTextual(string type)
        => type.Contains("TEXT", StringComparison.OrdinalIgnoreCase)
           || type.Contains("CHAR", StringComparison.OrdinalIgnoreCase)
           || type.Contains("CLOB", StringComparison.OrdinalIgnoreCase)
           || type.Length == 0;

    private static IReadOnlyList<ColumnInfo> ReadColumns(SqliteConnection connection, SqliteTransaction tx, string table)
    {
        var list = new List<ColumnInfo>();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({Q(table)})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // cid, name, type, notnull, dflt_value, pk
            list.Add(new ColumnInfo(
                reader.GetString(1),
                NotNull: reader.GetInt32(3) == 1,
                HasDefault: !reader.IsDBNull(4),
                IsKey: reader.GetInt32(5) > 0,
                Type: reader.GetString(2)));
        }
        return list;
    }

    private static void Exec(SqliteConnection connection, SqliteTransaction? tx, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
