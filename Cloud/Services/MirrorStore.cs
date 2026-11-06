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
        }

        _ready[siteId] = true;
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
        foreach (var table in tables)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {SyncSchema.Q(table)}"; // names come from code, never from a request
            result[table] = (long)cmd.ExecuteScalar()!;
        }
        return result;
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
