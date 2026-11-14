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
    /// <summary>
    /// SQL Server connection string for the control database (clients, subscriptions, tokens, accounts).
    /// Empty means SQLite (control.db in DataRoot), which is what tests, a local trial and a free test host use.
    /// Shop mirrors are SQLite files either way.
    /// </summary>
    public string ControlConnectionString { get; set; } = "";
    /// <summary>
    /// Refuse shop data that is not end-to-end encrypted. Leave off only while something still reads the copy on the server
    /// (the phone's pick-lists); turn on for production so the provider can never be handed readable shop data.
    /// </summary>
    public bool RequireEncryption { get; set; }    /// <summary>Admin portal account. The environment is the source of truth: restart with a new password to change it.</summary>
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

        // Encrypted rows: the provider can count them by table but not read them.
        if (HasEncryptedTable(connection))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT TableName, COUNT(*) FROM {SyncSchema.Q(MirrorApplier.EncryptedTable)} GROUP BY TableName";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = result.GetValueOrDefault(reader.GetString(0)) + reader.GetInt64(1);
        }
        return result;
    }

    /// <summary>True once this shop has sent end-to-end encrypted rows. The server cannot answer questions about such a copy; the phone does.</summary>
    public bool HasEncryptedRows(Guid siteId)
    {
        if (!Exists(siteId)) return false;
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = PathFor(siteId),
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        if (!HasEncryptedTable(connection)) return false;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT EXISTS(SELECT 1 FROM {SyncSchema.Q(MirrorApplier.EncryptedTable)})";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    /// <summary>One end-to-end encrypted row as the cloud holds it: opaque to the server, readable only by whoever has the shop's key.</summary>
    public sealed record EncryptedRow(long Id, string Payload, string UpdatedAt);

    /// <summary>
    /// Up to <paramref name="limit"/> encrypted rows of one table, oldest arrival first, strictly after the cursor
    /// (<paramref name="afterUpdatedAt"/>, <paramref name="afterId"/>). The table name is a bound parameter, never part of the SQL.
    /// </summary>
    public IReadOnlyList<EncryptedRow> ReadEncryptedRows(Guid siteId, string table, string? afterUpdatedAt, long afterId, int limit)
    {
        var rows = new List<EncryptedRow>();
        if (!Exists(siteId)) return rows;
        using var connection = OpenReadOnly(siteId);
        if (!HasEncryptedTable(connection)) return rows;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT RowId, Payload, UpdatedAt FROM {SyncSchema.Q(MirrorApplier.EncryptedTable)}
            WHERE TableName = $t AND ($u IS NULL OR UpdatedAt > $u OR (UpdatedAt = $u AND RowId > $id))
            ORDER BY UpdatedAt, RowId LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$t", table);
        cmd.Parameters.AddWithValue("$u", (object?)afterUpdatedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", afterId);
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) rows.Add(new EncryptedRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    /// <summary>Ids of every encrypted row of one table. A phone uses it to drop rows the shop has since deleted.</summary>
    public IReadOnlyList<long> EncryptedRowIds(Guid siteId, string table)
    {
        var ids = new List<long>();
        if (!Exists(siteId)) return ids;
        using var connection = OpenReadOnly(siteId);
        if (!HasEncryptedTable(connection)) return ids;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT RowId FROM {SyncSchema.Q(MirrorApplier.EncryptedTable)} WHERE TableName = $t ORDER BY RowId";
        cmd.Parameters.AddWithValue("$t", table);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt64(0));
        return ids;
    }

    private SqliteConnection OpenReadOnly(Guid siteId)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = PathFor(siteId), Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        return connection;
    }

    private static bool HasEncryptedTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{MirrorApplier.EncryptedTable}'";
        return cmd.ExecuteScalar() is not null;
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
