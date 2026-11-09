using Microsoft.Data.Sqlite;
using static Sync.SyncSchema;

namespace Sync;

/// <summary>
/// Change capture for the till database. An AFTER INSERT/UPDATE/DELETE trigger on every synced table appends
/// (table, row id, op) to <c>__SyncLog</c> inside the SAME transaction as the business write.
/// <para>
/// Triggers rather than an EF interceptor on purpose: the app writes with <c>ExecuteUpdateAsync</c>, raw SQL and
/// bulk import, none of which pass through SaveChanges, and a crash between "data committed" and "log written"
/// would silently lose a sale from the mirror. The log holds no values, only addresses; the pusher reads the
/// current row when it sends, so many edits of one row cost one change.
/// </para>
/// <para>EF migrations that rebuild a SQLite table drop its triggers, so <see cref="Install"/> runs on every start, after migrating.</para>
/// </summary>
public static class SyncTriggers
{
    public const string LogTable = "__SyncLog";
    public const string StateTable = "__SyncState";

    public const string AckedSeqKey = "AckedSeq";
    public const string SeededKey = "Seeded";
    public const string NeedsInitialKey = "NeedsInitial";
    /// <summary>Id of the seal key the cloud's copy was last (re)built under. A different key means upload everything again, sealed.</summary>
    public const string SealedKeyKey = "SealedKeyId";

    private static string TriggerName(string table, char kind) => $"__sync_{table}_{kind}";

    public static void EnsureTables(SqliteConnection connection)
    {
        Exec(connection, $"""
            CREATE TABLE IF NOT EXISTS {Q(LogTable)} (
                Seq INTEGER PRIMARY KEY AUTOINCREMENT,
                TableName TEXT NOT NULL,
                RowId INTEGER NOT NULL,
                Op TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS {Q(StateTable)} (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """);
    }

    /// <summary>Creates the bookkeeping tables and any missing trigger. Safe to call on every start.</summary>
    public static void Install(SqliteConnection connection, SyncSchema schema)
    {
        EnsureTables(connection);
        using var tx = connection.BeginTransaction();
        foreach (var table in schema.Tables)
        {
            if (!TableExists(connection, tx, table.Name)) continue;
            var t = Q(table.Name);
            var key = Q(table.KeyColumn);
            Exec(connection, tx, $"""
                CREATE TRIGGER IF NOT EXISTS {Q(TriggerName(table.Name, 'i'))} AFTER INSERT ON {t}
                BEGIN INSERT INTO {Q(LogTable)}(TableName, RowId, Op) VALUES ('{table.Name}', NEW.{key}, 'U'); END;
                CREATE TRIGGER IF NOT EXISTS {Q(TriggerName(table.Name, 'u'))} AFTER UPDATE ON {t}
                BEGIN INSERT INTO {Q(LogTable)}(TableName, RowId, Op) VALUES ('{table.Name}', NEW.{key}, 'U'); END;
                CREATE TRIGGER IF NOT EXISTS {Q(TriggerName(table.Name, 'd'))} AFTER DELETE ON {t}
                BEGIN INSERT INTO {Q(LogTable)}(TableName, RowId, Op) VALUES ('{table.Name}', OLD.{key}, 'D'); END;
                """);
        }
        tx.Commit();
    }

    /// <summary>
    /// Switches capture off (online disabled). The log is cleared and the snapshot flag reset, so turning
    /// online back on later starts with a full resync instead of a log with a hole in it.
    /// </summary>
    public static void Uninstall(SqliteConnection connection, SyncSchema schema)
    {
        // Never enrolled: leave the database exactly as it is (no bookkeeping tables, nothing to drop).
        if (!TableExists(connection, null, LogTable)) return;

        using var tx = connection.BeginTransaction();
        foreach (var table in schema.Tables)
        foreach (var kind in new[] { 'i', 'u', 'd' })
            Exec(connection, tx, $"DROP TRIGGER IF EXISTS {Q(TriggerName(table.Name, kind))};");
        Exec(connection, tx, $"DELETE FROM {Q(LogTable)};");
        if (TableExists(connection, tx, StateTable))
            Exec(connection, tx, $"DELETE FROM {Q(StateTable)} WHERE Key IN ('{SeededKey}', '{NeedsInitialKey}', '{SealedKeyKey}');");
        tx.Commit();
    }

    /// <summary>Queues every existing row as an upsert and marks the next batch as the initial (mirror-wiping) one.</summary>
    public static void Seed(SqliteConnection connection, SyncSchema schema)
    {
        using var tx = connection.BeginTransaction();
        Exec(connection, tx, $"DELETE FROM {Q(LogTable)};");
        foreach (var table in schema.Tables)
        {
            if (!TableExists(connection, tx, table.Name)) continue;
            Exec(connection, tx,
                $"INSERT INTO {Q(LogTable)}(TableName, RowId, Op) SELECT '{table.Name}', {Q(table.KeyColumn)}, 'U' FROM {Q(table.Name)} ORDER BY {Q(table.KeyColumn)};");
        }
        SetState(connection, tx, SeededKey, "1");
        SetState(connection, tx, NeedsInitialKey, "1");
        tx.Commit();
    }

    public static string? GetState(SqliteConnection connection, string key, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT Value FROM {Q(StateTable)} WHERE Key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public static void SetState(SqliteConnection connection, SqliteTransaction? tx, string key, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {Q(StateTable)}(Key, Value) VALUES ($k, $v) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public static long PendingCount(SqliteConnection connection)
    {
        var acked = long.Parse(GetState(connection, AckedSeqKey) ?? "0");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {Q(LogTable)} WHERE Seq > $a";
        cmd.Parameters.AddWithValue("$a", acked);
        return (long)cmd.ExecuteScalar()!;
    }

    private static bool TableExists(SqliteConnection connection, SqliteTransaction? tx, string name)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    private static void Exec(SqliteConnection connection, string sql) => Exec(connection, null, sql);

    private static void Exec(SqliteConnection connection, SqliteTransaction? tx, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
