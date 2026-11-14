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

    /// <summary>Where end-to-end encrypted rows live: the cloud sees the table, the row id and an opaque payload, nothing else.</summary>
    public const string EncryptedTable = "__EncryptedRows";
    private const int MaxPayloadChars = 12 * 1024 * 1024;

    public static string EnsureEncryptedTableSql => $"""
        CREATE TABLE IF NOT EXISTS {Q(EncryptedTable)} (
            TableName TEXT NOT NULL,
            RowId INTEGER NOT NULL,
            Payload TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            PRIMARY KEY (TableName, RowId)
        ) WITHOUT ROWID;
        """;

    /// <param name="requireEncrypted">Refuse readable rows. Turn on once nothing that reads the copy needs them.</param>
    /// <returns>Number of changes applied.</returns>
    public int Apply(string mirrorPath, SyncBatch batch, bool requireEncrypted = false)
    {
        if (requireEncrypted && batch.Changes.Any(c => !c.Deleted && c.Enc is null))
            throw new SyncBatchException("This cloud only accepts end-to-end encrypted data. Update NexPOS on the till.");
        if (batch.Changes.Any(c => c.Enc is not null && (!c.Enc.StartsWith(RowCipher.Prefix, StringComparison.Ordinal) || c.Enc.Length > MaxPayloadChars)))
            throw new SyncBatchException("A change carries a payload that is not valid encrypted data.");

        if (batch.Changes.Count > 0 && batch.Changes.Any(c => !schema.TryGetTable(c.Table, out _)))
        {
            var unknown = batch.Changes.First(c => !schema.TryGetTable(c.Table, out _)).Table;
            throw new SyncBatchException($"Unknown table '{unknown}'.");
        }

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
        using var tx = connection.BeginTransaction();

        var now = DateTime.UtcNow.ToString("O");
        if (batch.Initial || batch.Changes.Any(c => c.Enc is not null))
            Exec(connection, tx, EnsureEncryptedTableSql);

        if (batch.Initial)
        {
            foreach (var table in schema.Tables)
                Exec(connection, tx, $"DELETE FROM {Q(table.Name)};");
            Exec(connection, tx, $"DELETE FROM {Q(EncryptedTable)};");
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

                if (TableExists(connection, tx, EncryptedTable))
                {
                    using var delEnc = connection.CreateCommand();
                    delEnc.Transaction = tx;
                    delEnc.CommandText = $"DELETE FROM {Q(EncryptedTable)} WHERE TableName = $t AND RowId = $id";
                    delEnc.Parameters.AddWithValue("$t", table.Name);
                    delEnc.Parameters.AddWithValue("$id", change.Id);
                    delEnc.ExecuteNonQuery();
                }
            }
            else if (change.Enc is not null)
            {
                using var put = connection.CreateCommand();
                put.Transaction = tx;
                put.CommandText = $"INSERT INTO {Q(EncryptedTable)} (TableName, RowId, Payload, UpdatedAt) VALUES ($t, $id, $p, $u) " +
                                  "ON CONFLICT(TableName, RowId) DO UPDATE SET Payload = excluded.Payload, UpdatedAt = excluded.UpdatedAt";
                put.Parameters.AddWithValue("$t", table.Name);
                put.Parameters.AddWithValue("$id", change.Id);
                put.Parameters.AddWithValue("$p", change.Enc);
                put.Parameters.AddWithValue("$u", now);
                put.ExecuteNonQuery();
            }
            else
            {
                if (change.Values is null) throw new SyncBatchException($"Change for {change.Table}#{change.Id} has no values.");
                if (!columnCache.TryGetValue(table.Name, out var actual))
                    columnCache[table.Name] = actual = ReadColumns(connection, tx, table.Name);
                Upsert(connection, tx, table, actual, change);
            }
            applied++;
        }

        tx.Commit();
        return applied;
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

    private static bool TableExists(SqliteConnection connection, SqliteTransaction tx, string name)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    private static void Exec(SqliteConnection connection, SqliteTransaction tx, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
