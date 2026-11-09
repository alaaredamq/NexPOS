using System.Collections.Concurrent;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sync;

namespace Cloud.Services;

public sealed class CloudOptions
{
    /// <summary>Folder for control.db and the per-site mirror files. Put it on a persistent volume and back it up.</summary>
    public string DataRoot { get; set; } = "data";
    /// <summary>Admin portal account. The environment is the source of truth: restart with a new password to change it.</summary>
    public string AdminUser { get; set; } = "";
    public string AdminPassword { get; set; } = "";
    /// <summary>Public address of this site (https://cloud.example.com). Used to build enrolment links; falls back to the address the portal was opened on.</summary>
    public string PublicUrl { get; set; } = "";
    /// <summary>Time zone the portal shows times in (IANA name). Data is always stored in UTC.</summary>
    public string TimeZone { get; set; } = "Asia/Beirut";
}

/// <param name="LockedRows">Rows the shop sealed: stored, counted, unreadable here.</param>
/// <param name="ReadableRows">Rows of tables that should be sealed, still readable in this copy.</param>
/// <param name="KeyIds">Public fingerprints of the key(s) the locked rows are sealed under (normally one).</param>
public sealed record MirrorPrivacy(long LockedRows, long ReadableRows, IReadOnlyList<string> KeyIds);

/// <summary>
/// One SQLite file per site, same schema as the till. Isolation is physical: a bug in a query cannot leak
/// across clients because no two clients share a database file, and the file name is derived from a GUID
/// we generated, never from anything a till sends.
/// </summary>
public sealed class MirrorStore(IOptions<CloudOptions> options)
{
    private readonly ConcurrentDictionary<Guid, bool> _ready = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly string _root = Path.GetFullPath(options.Value.DataRoot);

    /// <summary>Newest migration this cloud build knows. A till reporting a later one is refused.</summary>
    public string LatestMigration { get; } = ReadLatestMigration();

    public string PathFor(Guid siteId) => Path.Combine(_root, "sites", siteId.ToString("N") + ".db");

    public bool Exists(Guid siteId) => File.Exists(PathFor(siteId));

    /// <summary>Serialises writes to one site; different sites never wait on each other.</summary>
    public SemaphoreSlim LockFor(Guid siteId) => _locks.GetOrAdd(siteId, _ => new SemaphoreSlim(1, 1));

    /// <summary>Creates the mirror on first use and brings it to this build's schema (also after a cloud upgrade).</summary>
    public void EnsureReady(Guid siteId)
    {
        if (_ready.ContainsKey(siteId)) return;
        var path = PathFor(siteId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var context = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlite(connectionString).Options);
        context.Database.Migrate();

        // WAL: the portal reads row counts while a till is writing.
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
            MirrorApplier.EnsureSealedTable(connection);
        }

        _ready[siteId] = true;
    }

    /// <summary>
    /// Deletes a site's mirror file for good. Waits for a push that is in flight, and drops pooled connections first
    /// because Windows will not delete a file that is still open.
    /// </summary>
    public async Task DeleteAsync(Guid siteId)
    {
        var gate = LockFor(siteId);
        await gate.WaitAsync();
        try
        {
            SqliteConnection.ClearAllPools();
            var path = PathFor(siteId);
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
            _ready.TryRemove(siteId, out _);
        }
        finally { gate.Release(); }
    }

    public IReadOnlyDictionary<string, long> CountRows(Guid siteId, IEnumerable<string> tables)
    {
        var result = new Dictionary<string, long>();
        if (!Exists(siteId)) return result;

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = PathFor(siteId),
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        var hasSealed = Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{MirrorApplier.SealedTable}'") > 0;
        foreach (var table in tables)
        {
            // Names come from code, never from a request.
            var count = Scalar(connection, $"SELECT COUNT(*) FROM {SyncSchema.Q(table)}");

            // A sealed table has no readable rows here, only locked ones; the cloud can still count those. The larger of the
            // two, because a shop that was readable before and sealed since may briefly have both until its full resync.
            if (hasSealed && SealPolicy.IsFullySealed(table))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {SyncSchema.Q(MirrorApplier.SealedTable)} WHERE TableName = $t";
                cmd.Parameters.AddWithValue("$t", table);
                count = Math.Max(count, (long)cmd.ExecuteScalar()!);
            }
            result[table] = count;
        }
        return result;
    }

    /// <summary>
    /// How private a branch's cloud copy is, from what the file holds: locked rows (<see cref="MirrorApplier.SealedTable"/>) and any rows
    /// of the sealed tables still readable. A branch that switched to sealed has both until its full re-upload has replaced the readable ones.
    /// </summary>
    public MirrorPrivacy Privacy(Guid siteId)
    {
        if (!Exists(siteId)) return new MirrorPrivacy(0, 0, []);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = PathFor(siteId),
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();

        long locked = 0, readable = 0;
        var keys = new List<string>();
        if (Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{MirrorApplier.SealedTable}'") > 0)
        {
            locked = Scalar(connection, $"SELECT COUNT(*) FROM {SyncSchema.Q(MirrorApplier.SealedTable)}");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT DISTINCT KeyId FROM {SyncSchema.Q(MirrorApplier.SealedTable)} ORDER BY KeyId LIMIT 5";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) keys.Add(reader.GetString(0));
        }

        // The sealed tables are sealed by the till; any row of them in its readable form is data this server can read.
        foreach (var table in SealPolicy.Sealed)
            readable += Scalar(connection, $"SELECT COUNT(*) FROM {SyncSchema.Q(table)}");
        return new MirrorPrivacy(locked, readable, keys);
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    private static string ReadLatestMigration()
    {
        using var context = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlite("Data Source=:memory:").Options);
        return context.Database.GetMigrations().LastOrDefault() ?? "";
    }

    /// <summary>The synced table list, taken from the model without opening any database.</summary>
    public static SyncSchema BuildSchema()
    {
        using var context = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlite("Data Source=:memory:").Options);
        return SyncSchema.FromModel(context.Model);
    }
}
