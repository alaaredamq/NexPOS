using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Tests.Migrations;

/// <summary>
/// The safety net around the startup upgrade: a verified copy before, a check after, the copy put back when anything is wrong,
/// and no upgrade at all when the copy cannot be made. Uses real databases on disk, including a real older-to-newer upgrade.
/// </summary>
public class PreUpgradeGuardTests : IDisposable
{
    private const string OldVersion = "20260922180603_AddCustomerDebtTracking";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "nexpos-guard-" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_folder, "shop.db");
    private string Backups => Path.Combine(_folder, "backups");

    public PreUpgradeGuardTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlite($"Data Source={DbPath}").Options);

    private static long Count(string path, string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static void Exec(string path, string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>A shop database at the previous schema version, holding a real product.</summary>
    private async Task<List<string>> OldShopAsync()
    {
        using var old = Context();
        await old.GetService<IMigrator>().MigrateAsync(OldVersion);
        var conn = old.Database.GetDbConnection();
        await conn.OpenAsync();
        await MigrationUpgradeTests.InsertRow(conn, "Products", new Dictionary<string, object>
        {
            ["Name"] = "Guarded Widget", ["Barcode"] = "GRD-001",
            ["SupplierId"] = Convert.ToInt32(await MigrationUpgradeTests.Scalar(conn, "SELECT Id FROM Suppliers LIMIT 1")),
            ["CategoryId"] = Convert.ToInt32(await MigrationUpgradeTests.Scalar(conn, "SELECT Id FROM ProductCategories LIMIT 1")),
            ["CurrencyId"] = Convert.ToInt32(await MigrationUpgradeTests.Scalar(conn, "SELECT Id FROM Currencies LIMIT 1")),
        });
        return (await old.Database.GetPendingMigrationsAsync()).ToList();
    }

    [Fact]
    public async Task A_real_upgrade_is_copied_first_verified_and_loses_nothing()
    {
        var pending = await OldShopAsync();
        Assert.NotEmpty(pending);
        SqliteConnection.ClearAllPools();

        var guard = PreUpgradeGuard.Begin(DbPath, pending, Backups)!;
        Assert.NotNull(guard);
        Assert.True(File.Exists(guard.BackupPath));
        Assert.Equal(1L, Count(guard.BackupPath, "SELECT COUNT(*) FROM Products WHERE Barcode = 'GRD-001'")); // the copy holds the data

        using (var ctx = Context()) await ctx.Database.MigrateAsync();
        guard.VerifyAfterUpgrade(); // must not throw: nothing lost, no broken links
        guard.Commit();

        Assert.Equal(1L, Count(DbPath, "SELECT COUNT(*) FROM Products WHERE Barcode = 'GRD-001'"));
        Assert.True(File.Exists(guard.BackupPath), "the safety copy stays after a good upgrade");
        Assert.Equal(pending, guard.PendingMigrations.ToList());
    }


    [Fact]
    public async Task Nothing_is_copied_when_there_is_nothing_to_upgrade()
    {
        await OldShopAsync();
        using (var ctx = Context()) await ctx.Database.MigrateAsync();
        SqliteConnection.ClearAllPools();

        Assert.Null(PreUpgradeGuard.Begin(DbPath, [], Backups));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void A_fresh_install_has_nothing_to_protect_so_no_copy_is_made()
    {
        Assert.Null(PreUpgradeGuard.Begin(DbPath, ["20260101000000_Anything"], Backups)); // no file at all

        using (var c = new SqliteConnection($"Data Source={DbPath}")) { c.Open(); } // an empty file: no history table yet
        Assert.Null(PreUpgradeGuard.Begin(DbPath, ["20260101000000_Anything"], Backups));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public async Task A_bad_upgrade_that_loses_rows_is_detected_and_the_copy_puts_everything_back()
    {
        var pending = await OldShopAsync();
        SqliteConnection.ClearAllPools();
        var guard = PreUpgradeGuard.Begin(DbPath, pending, Backups)!;
        using (var ctx = Context()) await ctx.Database.MigrateAsync();

        // A migration that went wrong and silently deleted customer data.
        Exec(DbPath, "DELETE FROM Products");

        var failure = Assert.Throws<UpgradeVerificationException>(guard.VerifyAfterUpgrade);
        Assert.Contains("Products", failure.Message);

        guard.Restore();
        Assert.Equal(1L, Count(DbPath, "SELECT COUNT(*) FROM Products WHERE Barcode = 'GRD-001'"));
        Assert.Equal(OldVersion, LastAppliedMigration(DbPath)); // back on the version before the upgrade
    }

    [Fact]
    public async Task A_migration_that_deletes_rows_on_purpose_can_say_so()
    {
        var pending = await OldShopAsync();
        SqliteConnection.ClearAllPools();
        var guard = PreUpgradeGuard.Begin(DbPath, pending, Backups, allowShrinkingTables: ["Products"])!;
        using (var ctx = Context()) await ctx.Database.MigrateAsync();
        Exec(DbPath, "DELETE FROM Products");

        guard.VerifyAfterUpgrade(); // allowed for that table: no exception
    }

    [Fact]
    public async Task New_broken_links_after_an_upgrade_are_caught()
    {
        var pending = await OldShopAsync();
        SqliteConnection.ClearAllPools();
        var guard = PreUpgradeGuard.Begin(DbPath, pending, Backups)!;
        using (var ctx = Context()) await ctx.Database.MigrateAsync();

        Exec(DbPath, "PRAGMA foreign_keys = OFF; DELETE FROM Suppliers;"); // the product now points at a supplier that is gone

        var failure = Assert.Throws<UpgradeVerificationException>(guard.VerifyAfterUpgrade);
        Assert.True(failure.Message.Contains("pointing") || failure.Message.Contains("Suppliers"), failure.Message);
    }

    [Fact]
    public async Task When_the_copy_cannot_be_made_the_upgrade_is_stopped_and_the_data_untouched()
    {
        var pending = await OldShopAsync();
        SqliteConnection.ClearAllPools();
        var before = File.ReadAllBytes(DbPath);

        // A file where the backup FOLDER should be: copying is impossible, whatever the reason in real life (disk full, no permission).
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "x");

        var ex = Assert.Throws<PreUpgradeBackupException>(() => PreUpgradeGuard.Begin(DbPath, pending, blocked));
        Assert.Contains("NOT run", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    [Fact]
    public async Task Only_the_newest_copies_are_kept_and_the_latest_always_stays()
    {
        var pending = await OldShopAsync();
        SqliteConnection.ClearAllPools();
        Directory.CreateDirectory(Backups);
        for (var i = 0; i < 6; i++)
        {
            var old = Path.Combine(Backups, $"nexpos-2026010{i}-000000-before-x.db");
            File.WriteAllText(old, "old");
            File.SetCreationTimeUtc(old, DateTime.UtcNow.AddDays(-30 + i));
        }

        var guard = PreUpgradeGuard.Begin(DbPath, pending, Backups, keep: 3)!;
        guard.Commit();

        var left = Directory.GetFiles(Backups, "nexpos-*.db");
        Assert.Equal(3, left.Length);
        Assert.Contains(left, f => f == guard.BackupPath);
    }


    // ---- power cut in the middle of an upgrade ----

    [Fact]
    public async Task A_power_cut_during_the_upgrade_is_healed_on_the_next_start()
    {
        var pending = await OldShopAsync();
        SqliteConnection.ClearAllPools();
        var guard = PreUpgradeGuard.Begin(DbPath, pending, Backups)!;

        // The upgrade starts, damages the database, and the process dies: no Verify, no Restore, no Commit.
        Exec(DbPath, "DELETE FROM Products; CREATE TABLE HalfDone (Id INTEGER PRIMARY KEY);");
        Assert.Equal(0L, Count(DbPath, "SELECT COUNT(*) FROM Products"));
        Assert.True(File.Exists(Path.Combine(Backups, "upgrade-in-progress.txt")));

        // Next start.
        var restored = PreUpgradeGuard.RecoverInterruptedUpgrade(DbPath, Backups);

        Assert.Equal(guard.BackupPath, restored);
        Assert.Equal(1L, Count(DbPath, "SELECT COUNT(*) FROM Products WHERE Barcode = 'GRD-001'"));
        Assert.Equal(0L, Count(DbPath, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'HalfDone'"));
        Assert.Equal(OldVersion, LastAppliedMigration(DbPath));
        Assert.False(File.Exists(Path.Combine(Backups, "upgrade-in-progress.txt")));
        Assert.Null(PreUpgradeGuard.RecoverInterruptedUpgrade(DbPath, Backups)); // nothing left to heal
    }

    [Fact]
    public async Task A_finished_upgrade_leaves_nothing_to_heal()
    {
        var pending = await OldShopAsync();
        SqliteConnection.ClearAllPools();
        var guard = PreUpgradeGuard.Begin(DbPath, pending, Backups)!;
        using (var ctx = Context()) await ctx.Database.MigrateAsync();
        guard.VerifyAfterUpgrade();
        guard.Commit();

        Assert.False(File.Exists(Path.Combine(Backups, "upgrade-in-progress.txt")));
        Assert.Null(PreUpgradeGuard.RecoverInterruptedUpgrade(DbPath, Backups));
        Assert.Equal(1L, Count(DbPath, "SELECT COUNT(*) FROM Products WHERE Barcode = 'GRD-001'")); // and nothing was rolled back
    }

    [Fact]
    public async Task A_restore_after_a_failed_upgrade_also_clears_the_marker()
    {
        var pending = await OldShopAsync();
        SqliteConnection.ClearAllPools();
        var guard = PreUpgradeGuard.Begin(DbPath, pending, Backups)!;
        guard.Restore();

        Assert.False(File.Exists(Path.Combine(Backups, "upgrade-in-progress.txt")));
        Assert.Null(PreUpgradeGuard.RecoverInterruptedUpgrade(DbPath, Backups));
    }

    [Fact]
    public void A_damaged_safety_copy_is_never_restored_over_the_database()
    {
        Directory.CreateDirectory(Backups);
        File.WriteAllText(DbPath, "the current database");
        var bad = Path.Combine(Backups, "nexpos-x.db");
        File.WriteAllText(bad, "this is not a database");
        File.WriteAllText(Path.Combine(Backups, "upgrade-in-progress.txt"), bad);

        Assert.Throws<PreUpgradeBackupException>(() => PreUpgradeGuard.RecoverInterruptedUpgrade(DbPath, Backups));
        Assert.Equal("the current database", File.ReadAllText(DbPath));
    }

    [Fact]
    public void Nothing_happens_at_start_when_no_upgrade_was_interrupted()
        => Assert.Null(PreUpgradeGuard.RecoverInterruptedUpgrade(DbPath, Backups));

    private static string? LastAppliedMigration(string path)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId DESC LIMIT 1";
        return cmd.ExecuteScalar() as string;
    }
}
