using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Tests.Migrations;

/// <summary>
/// AddSupplierDebtTracking drops two Suppliers columns, which makes EF's SQLite provider rebuild the
/// table (create new, copy, drop old, rename) and warn that its "PRAGMA foreign_keys = 0" can't run
/// inside the migration transaction. If foreign keys stay on, dropping the old Suppliers table could
/// cascade into Products. This upgrades a database that already holds data and checks nothing is lost.
/// </summary>
public class MigrationUpgradeTests : IDisposable
{
    private const string BeforeSupplierDebt = "20260922180603_AddCustomerDebtTracking";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nexpos-upgrade-{Guid.NewGuid():N}.db");

    private ErpDbContext CreateContext() => new(new DbContextOptionsBuilder<ErpDbContext>()
        .UseSqlite($"Data Source={_dbPath}")
        .Options);

    [Fact]
    public async Task UpgradingAPopulatedDatabase_KeepsProductsAndForeignKeysIntact()
    {
        int supplierId, categoryId, currencyId;
        using (var old = CreateContext())
        {
            await old.GetService<IMigrator>().MigrateAsync(BeforeSupplierDebt);
            var conn = old.Database.GetDbConnection();
            await conn.OpenAsync();

            supplierId = Convert.ToInt32(await Scalar(conn, "SELECT Id FROM Suppliers ORDER BY Id LIMIT 1"));
            currencyId = Convert.ToInt32(await Scalar(conn, "SELECT Id FROM Currencies ORDER BY Id LIMIT 1"));
            categoryId = Convert.ToInt32(await Scalar(conn, "SELECT Id FROM ProductCategories ORDER BY Id LIMIT 1"));

            await InsertRow(conn, "Products", new Dictionary<string, object>
            {
                ["Name"] = "Upgrade Widget",
                ["Barcode"] = "UPG-001",
                ["SupplierId"] = supplierId,
                ["CategoryId"] = categoryId,
                ["CurrencyId"] = currencyId,
            });
            Assert.Equal(1L, await Scalar(conn, "SELECT COUNT(*) FROM Products WHERE Barcode = 'UPG-001'"));
        }

        using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());

            var conn = upgraded.Database.GetDbConnection();
            await conn.OpenAsync();

            Assert.Equal(1L, await Scalar(conn, "SELECT COUNT(*) FROM Products WHERE Barcode = 'UPG-001'"));
            Assert.Equal(1L, await Scalar(conn, $"SELECT COUNT(*) FROM Suppliers WHERE Id = {supplierId}"));
            Assert.Equal(0L, await Scalar(conn, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
        }
    }

    private static async Task<object?> Scalar(System.Data.Common.DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    /// <summary>Inserts one row, filling every other NOT NULL column that has no default with a neutral value.</summary>
    private static async Task InsertRow(System.Data.Common.DbConnection conn, string table, Dictionary<string, object> values)
    {
        var row = new Dictionary<string, object>(values);
        await using (var info = conn.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info('{table}')";
            await using var reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                var type = reader.GetString(2).ToUpperInvariant();
                var notNull = reader.GetInt32(3) == 1;
                var hasDefault = !reader.IsDBNull(4);
                var isPk = reader.GetInt32(5) > 0;
                if (row.ContainsKey(name) || isPk || !notNull || hasDefault) continue;
                row[name] = type.Contains("INT") ? 0 : type.Contains("TEXT") ? "" : type.Contains("DATE") ? "2026-01-01 00:00:00" : 0;
            }
        }

        await using var insert = conn.CreateCommand();
        insert.CommandText = $"INSERT INTO \"{table}\" ({string.Join(",", row.Keys.Select(k => $"\"{k}\""))}) VALUES ({string.Join(",", row.Keys.Select((_, i) => $"$p{i}"))})";
        var n = 0;
        foreach (var v in row.Values)
        {
            var p = insert.CreateParameter();
            p.ParameterName = $"$p{n++}";
            p.Value = v;
            insert.Parameters.Add(p);
        }
        await insert.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { }
        }
    }
}
