using Microsoft.Data.Sqlite;

namespace Infrastructure.Data;

/// <summary>The upgrade was stopped before touching the data (the safety copy could not be made or did not verify).</summary>
public sealed class PreUpgradeBackupException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The upgrade ran but the result looks wrong; the safety copy should be put back.</summary>
public sealed class UpgradeVerificationException(string message) : Exception(message);

/// <summary>
/// Protects a client's data while the app upgrades its own database at startup (packaged installs migrate automatically; nobody
/// is there to run anything by hand):
/// <list type="number">
/// <item><see cref="Begin"/> copies the whole database (SQLite <c>VACUUM INTO</c>, safe and consistent), proves the copy is sound
/// and complete, and only then lets the upgrade start. If the copy cannot be made the upgrade does not run.</item>
/// <item><see cref="VerifyAfterUpgrade"/> checks the result: database integrity, no new broken links between records, and no table
/// lost rows.</item>
/// <item><see cref="Restore"/> puts the copy back if the upgrade or the check failed, so the shop is never left on a half-upgraded database.</item>
/// <item><see cref="Commit"/> keeps the newest few copies and removes older ones.</item>
/// </list>
/// A fresh install (no data yet) and a start with nothing to upgrade are left alone: no copy is made.
/// A migration that intentionally deletes rows must say so in <c>allowShrinkingTables</c>, otherwise the check treats it as a loss.
/// </summary>
public sealed class PreUpgradeGuard
{
    public const string FolderName = "pre-upgrade-backups";
    private const string MarkerName = "upgrade-in-progress.txt";

    private readonly string _databasePath;
    private readonly IReadOnlyDictionary<string, long> _rowCountsBefore;
    private readonly long _brokenLinksBefore;
    private readonly IReadOnlySet<string> _allowShrink;
    private readonly string _backupFolder;
    private readonly int _keep;

    private PreUpgradeGuard(string databasePath, string backupPath, string backupFolder, int keep,
        IReadOnlyDictionary<string, long> rowCountsBefore, long brokenLinksBefore, IReadOnlySet<string> allowShrink,
        IReadOnlyList<string> pending)
    {
        _databasePath = databasePath;
        BackupPath = backupPath;
        _backupFolder = backupFolder;
        _keep = keep;
        _rowCountsBefore = rowCountsBefore;
        _brokenLinksBefore = brokenLinksBefore;
        _allowShrink = allowShrink;
        PendingMigrations = pending;
    }

    /// <summary>The verified safety copy.</summary>
    public string BackupPath { get; }

    public IReadOnlyList<string> PendingMigrations { get; }

    /// <returns>The guard, or null when there is nothing to protect (nothing pending, or a database that has never been upgraded before).</returns>
    /// <exception cref="PreUpgradeBackupException">The safety copy could not be made or failed its check. Do not upgrade.</exception>
    public static PreUpgradeGuard? Begin(
        string databasePath,
        IReadOnlyCollection<string> pendingMigrations,
        string? backupFolder = null,
        int keep = 5,
        IEnumerable<string>? allowShrinkingTables = null)
    {
        if (pendingMigrations.Count == 0 || !File.Exists(databasePath)) return null;

        using var source = Open(databasePath);
        if (!HasUpgradeHistory(source)) return null;

        var folder = backupFolder ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, FolderName);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var firstPending = pendingMigrations.First();
        var backupPath = Path.Combine(folder, $"nexpos-{stamp}-before-{firstPending}.db");

        IReadOnlyDictionary<string, long> counts;
        long brokenLinks;
        try
        {
            Directory.CreateDirectory(folder);
            counts = RowCounts(source);
            brokenLinks = BrokenLinks(source);

            using (var cmd = source.CreateCommand())
            {
                cmd.CommandText = "VACUUM INTO $path"; // refuses to overwrite: the timestamp makes the name unique
                cmd.Parameters.AddWithValue("$path", backupPath);
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw new PreUpgradeBackupException(
                $"The database could not be copied before the upgrade ({ex.Message}). The upgrade was NOT run and your data is untouched. " +
                "Free some disk space or check the folder permissions, then start NexPOS again.", ex);
        }

        // The copy has to be as good as the original, or it is no protection at all.
        try
        {
            using var copy = Open(backupPath, readOnly: true);
            var integrity = Scalar(copy, "PRAGMA integrity_check") as string;
            if (integrity != "ok") throw new PreUpgradeBackupException($"The safety copy failed its integrity check ({integrity}). The upgrade was not run.");

            var copyCounts = RowCounts(copy);
            foreach (var (table, rows) in counts)
            {
                if (!copyCounts.TryGetValue(table, out var copied) || copied != rows)
                    throw new PreUpgradeBackupException($"The safety copy does not match the database (table {table}: {rows} rows, copy has {copied}). The upgrade was not run.");
            }
        }
        catch (SqliteException ex)
        {
            throw new PreUpgradeBackupException($"The safety copy could not be read back ({ex.Message}). The upgrade was not run.", ex);
        }

        // From here until Commit/Restore an upgrade is in progress. If the power dies in this window, the next start finds this marker
        // and puts the copy back (RecoverInterruptedUpgrade) before trying again.
        File.WriteAllText(MarkerPath(folder), backupPath);

        return new PreUpgradeGuard(databasePath, backupPath, folder, keep, counts, brokenLinks,
            new HashSet<string>(allowShrinkingTables ?? [], StringComparer.OrdinalIgnoreCase), pendingMigrations.ToList());
    }

    /// <exception cref="UpgradeVerificationException">Something was lost or broken: call <see cref="Restore"/>.</exception>
    public void VerifyAfterUpgrade()
    {
        using var db = Open(_databasePath);

        var integrity = Scalar(db, "PRAGMA integrity_check") as string;
        if (integrity != "ok") throw new UpgradeVerificationException($"The database failed its integrity check after the upgrade ({integrity}).");

        var broken = BrokenLinks(db);
        if (broken > _brokenLinksBefore)
            throw new UpgradeVerificationException($"The upgrade left {broken - _brokenLinksBefore} record(s) pointing at something that no longer exists.");

        var after = RowCounts(db);
        foreach (var (table, before) in _rowCountsBefore)
        {
            if (!after.TryGetValue(table, out var now)) continue; // the table itself was removed on purpose by a migration
            if (now < before && !_allowShrink.Contains(table))
                throw new UpgradeVerificationException($"Table {table} had {before} rows before the upgrade and has {now} after.");
        }
    }

    /// <summary>Puts the safety copy back in place of the (possibly half-upgraded) database. Call only when nothing is using the database.</summary>
    public void Restore()
    {
        SqliteConnection.ClearAllPools(); // release the file so it can be replaced
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = _databasePath + suffix;
            if (File.Exists(sidecar)) File.Delete(sidecar);
        }
        File.Copy(BackupPath, _databasePath, overwrite: true);
        DeleteMarker(_backupFolder);
    }

    /// <summary>The upgrade is confirmed good: keep the newest copies, drop the rest. The copy just made always stays.</summary>
    public void Commit()
    {
        DeleteMarker(_backupFolder); // the upgrade is confirmed: nothing to recover any more
        if (!Directory.Exists(_backupFolder)) return;
        var old = new DirectoryInfo(_backupFolder).GetFiles("nexpos-*.db")
            .Where(f => !string.Equals(f.FullName, Path.GetFullPath(BackupPath), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(Math.Max(0, _keep - 1));
        foreach (var file in old)
        {
            try { file.Delete(); } catch (IOException) { /* an old copy that is in use can wait for next time */ }
        }
    }

    /// <summary>
    /// Call at startup, BEFORE looking at pending migrations. If the last upgrade never finished (power cut, crash, killed process)
    /// the database may be half-upgraded: put the copy made just before it back, so the upgrade can run again from a clean start.
    /// No business data can have been written in between: the app was still starting.
    /// </summary>
    /// <returns>The copy that was restored, or null when the last upgrade finished normally.</returns>
    public static string? RecoverInterruptedUpgrade(string databasePath, string? backupFolder = null)
    {
        var folder = backupFolder ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, FolderName);
        var marker = MarkerPath(folder);
        if (!File.Exists(marker)) return null;

        var backup = File.ReadAllText(marker).Trim();
        if (!File.Exists(backup)) { File.Delete(marker); return null; } // nothing left to restore from

        try
        {
            using var copy = Open(backup, readOnly: true);
            if (Scalar(copy, "PRAGMA integrity_check") as string != "ok")
                throw new PreUpgradeBackupException($"A previous upgrade was interrupted and its safety copy ({backup}) is damaged. Nothing was changed; contact support before starting NexPOS again.");
        }
        catch (SqliteException ex)
        {
            throw new PreUpgradeBackupException($"A previous upgrade was interrupted and its safety copy ({backup}) cannot be read ({ex.Message}). Nothing was changed; contact support before starting NexPOS again.", ex);
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar)) File.Delete(sidecar);
        }
        File.Copy(backup, databasePath, overwrite: true);
        File.Delete(marker);
        return backup;
    }

    private static string MarkerPath(string folder) => Path.Combine(folder, MarkerName);

    private static void DeleteMarker(string folder)
    {
        try { File.Delete(MarkerPath(folder)); } catch (IOException) { /* it will be handled on the next start */ }
    }

    private static SqliteConnection Open(string path, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false, // never leave a handle on a file that may need to be replaced
        }.ToString());
        connection.Open();
        return connection;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static bool HasUpgradeHistory(SqliteConnection connection)
    {
        try { return Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")) > 0; }
        catch (SqliteException) { return false; } // no history table: a database that was never created by the app
    }

    private static IReadOnlyDictionary<string, long> RowCounts(SqliteConnection connection)
    {
        var tables = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
            counts[table] = Convert.ToInt64(Scalar(connection, $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\""));
        return counts;
    }

    /// <summary>Number of records that point at a record that does not exist (a broken link). Zero in a healthy database.</summary>
    private static long BrokenLinks(SqliteConnection connection)
    {
        long count = 0;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) count++;
        return count;
    }
}
