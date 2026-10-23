using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Auditing;

/// <summary>Where the audit store lives and how it is written (the timings are only overridden by tests).</summary>
public sealed record AuditStoreOptions(
    string ConnectionString,
    string FallbackDirectory,
    TimeSpan[]? RetryDelays = null,
    TimeSpan? CoalesceWindow = null);

/// <summary>
/// Creates and upgrades the audit database. The store is one self-contained table that is only ever appended
/// to, so it is not worth a second migrations history and snapshot to keep in step with the business schema:
/// the schema version lives in <c>PRAGMA user_version</c> and later changes are applied here.
/// (Because this is a second <c>DbContext</c> in the assembly, <c>dotnet ef</c> commands need
/// <c>--context ErpDbContext</c> to say which one they mean.)
/// </summary>
public static class AuditDatabase
{
    /// <summary>Bump when the schema changes and add the upgrade step to <see cref="Upgrade"/>.</summary>
    public const int CurrentSchemaVersion = 1;

    public static DbContextOptions<AuditDbContext> CreateOptions(string connectionString)
        => new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connectionString).Options;

    /// <summary>Idempotent: creates the file and tables if needed, then brings an older schema up to date.</summary>
    public static void EnsureCreated(string connectionString)
    {
        using var db = new AuditDbContext(CreateOptions(connectionString));
        db.Database.EnsureCreated();

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        connection.Open();
        try
        {
            // Write-ahead logging: the viewer's reads and an export never block the writer (and vice versa).
            Execute(connection, "PRAGMA journal_mode = WAL;");

            var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"));
            if (version < CurrentSchemaVersion)
            {
                Upgrade(connection, version);
                Execute(connection, $"PRAGMA user_version = {CurrentSchemaVersion};");
            }
        }
        finally
        {
            connection.Close();
        }
    }

    private static void Upgrade(SqliteConnection connection, int fromVersion)
    {
        // v0 -> v1: the schema EnsureCreated just made. Future changes go here as
        //   if (fromVersion < 2) Execute(connection, "ALTER TABLE AuditLogs ADD COLUMN ...");
        _ = connection;
        _ = fromVersion;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
