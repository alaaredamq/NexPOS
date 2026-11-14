using System.Text.Json;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sync;
using Xunit;

namespace Cloud.Tests;

public class SchemaAndTriggerTests
{
    [Fact]
    public void Schema_covers_the_business_tables_and_keeps_credentials_and_local_state_out()
    {
        var schema = MirrorStore.BuildSchema();

        foreach (var table in new[] { "Products", "Customers", "Suppliers", "Orders", "Purchases", "Receipts", "CashDrawerSession", "ProductHistories" })
            Assert.True(schema.TryGetTable(table, out _), $"{table} should be synced");

        Assert.True(schema.TryGetTable("Users", out var users));
        Assert.DoesNotContain("PasswordHash", users.Columns);
        Assert.DoesNotContain("PinHash", users.Columns);
        Assert.Contains("Username", users.Columns);

        foreach (var table in new[] { "UserSessions", "SystemSettings", "AppSettings", "Carts", "Backups", "Notifications" })
            Assert.False(schema.TryGetTable(table, out _), $"{table} is local-only");

        // Anything skipped for lacking a single int key must not be a table that matters.
        Assert.DoesNotContain(schema.Skipped, t => t is "Products" or "Orders" or "Customers" or "Receipts");
    }

    [Fact]
    public async Task Triggers_log_inserts_updates_and_deletes_even_when_the_app_bypasses_SaveChanges()
    {
        using var till = new TillDb();
        till.EnableSync();

        var id = await till.AddCustomerAsync("Layla");
        await using (var context = till.CreateContext())
        {
            await context.Customers.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.City, "Saida"));
            await context.Customers.Where(c => c.Id == id).ExecuteDeleteAsync();
        }

        var ops = SqlLogOps(till, "Customers", id);
        Assert.Equal(["U", "U", "D"], ops);
    }

    [Fact]
    public void Foreign_key_cascades_fire_the_child_delete_trigger()
    {
        // The mirror applies deletes without cascading (FKs off), so the till MUST log cascaded child deletes itself.
        using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER NOT NULL REFERENCES Parent(Id) ON DELETE CASCADE);
            CREATE TABLE Log (Seq INTEGER PRIMARY KEY AUTOINCREMENT, T TEXT, R INTEGER, Op TEXT);
            CREATE TRIGGER c_d AFTER DELETE ON Child BEGIN INSERT INTO Log(T,R,Op) VALUES ('Child', OLD.Id, 'D'); END;
            INSERT INTO Parent VALUES (1); INSERT INTO Child VALUES (10, 1);
            DELETE FROM Parent WHERE Id = 1;
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT COUNT(*) FROM Log WHERE T = 'Child' AND R = 10 AND Op = 'D'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Install_is_repeatable_and_survives_a_dropped_trigger()
    {
        using var till = new TillDb();
        till.EnableSync();
        till.EnableSync(); // second start: no error, no duplicate triggers

        using (var connection = till.OpenRaw())
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TRIGGER \"__sync_Customers_i\"";
            drop.ExecuteNonQuery(); // what an EF table-rebuild migration does
        }
        till.EnableSync();

        var id = till.AddCustomerAsync("Omar").GetAwaiter().GetResult();
        Assert.Contains("U", SqlLogOps(till, "Customers", id));
    }

    [Fact]
    public void A_till_that_never_enrolled_is_left_untouched()
    {
        using var till = new TillDb();
        using (var connection = till.OpenRaw())
            SyncTriggers.Uninstall(connection, till.Schema);

        var tables = SqlHelper.Scalar<long>(till.Path,
            "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('__SyncLog', '__SyncState') OR name LIKE '__sync_%'");
        Assert.Equal(0L, tables);
    }

    [Fact]
    public async Task Disabling_online_removes_the_triggers_and_clears_the_log()
    {
        using var till = new TillDb();
        till.EnableSync();
        await till.AddCustomerAsync("Nour");

        using (var connection = till.OpenRaw())
            SyncTriggers.Uninstall(connection, till.Schema);

        Assert.Equal(0L, SqlHelper.Scalar<long>(till.Path, "SELECT COUNT(*) FROM \"__SyncLog\""));
        Assert.Equal(0L, SqlHelper.Scalar<long>(till.Path, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE '__sync_%'"));

        // Changes made while it was off must not be lost for good: re-enabling starts from a full snapshot.
        Assert.Null(SqlHelper.Scalar<string>(till.Path, "SELECT Value FROM \"__SyncState\" WHERE Key = 'Seeded'"));
    }

    [Fact]
    public void Blob_values_survive_the_wire_as_bytes_not_text()
    {
        var change = new SyncChange("Products", 1, false, new Dictionary<string, object?>
        {
            ["Image"] = new SyncBlob(Convert.ToBase64String([1, 2, 3])),
            ["Name"] = "AAEC", // looks like base64 but is text
            ["Price"] = 12.5,
            ["Qty"] = 3L,
            ["Note"] = null,
        });

        var json = JsonSerializer.Serialize(change, SyncWire.Json);
        var back = JsonSerializer.Deserialize<SyncChange>(json, SyncWire.Json)!;

        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])SyncWire.ToDbValue(back.Values!["Image"]));
        Assert.Equal("AAEC", SyncWire.ToDbValue(back.Values["Name"]));
        Assert.Equal(12.5, SyncWire.ToDbValue(back.Values["Price"]));
        Assert.Equal(3L, SyncWire.ToDbValue(back.Values["Qty"]));
        Assert.Equal(DBNull.Value, SyncWire.ToDbValue(back.Values["Note"]));
    }

    private static List<string> SqlLogOps(TillDb till, string table, int id)
    {
        var ops = new List<string>();
        using var connection = till.OpenRaw();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Op FROM \"__SyncLog\" WHERE TableName = $t AND RowId = $r ORDER BY Seq";
        cmd.Parameters.AddWithValue("$t", table);
        cmd.Parameters.AddWithValue("$r", id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) ops.Add(reader.GetString(0));
        return ops;
    }
}

public class SubscriptionRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static Cloud.Data.Subscription Sub(int daysFromNow, bool suspended = false)
        => new() { StartsAt = Now.AddYears(-1), ExpiresAt = Now.AddDays(daysFromNow), IsSuspended = suspended };

    [Theory]
    [InlineData(10, SubscriptionState.Active)]
    [InlineData(0, SubscriptionState.Active)]
    [InlineData(-1, SubscriptionState.Grace)]
    [InlineData(-7, SubscriptionState.Grace)]
    [InlineData(-8, SubscriptionState.Expired)]
    public void States_follow_the_expiry_date_with_a_grace_week(int daysFromNow, SubscriptionState expected)
        => Assert.Equal(expected, SubscriptionRules.Evaluate(Sub(daysFromNow), Now));

    [Fact]
    public void Suspension_and_missing_subscription_block_sync()
    {
        Assert.Equal(SubscriptionState.Suspended, SubscriptionRules.Evaluate(Sub(100, suspended: true), Now));
        Assert.Equal(SubscriptionState.None, SubscriptionRules.Evaluate(null, Now));
        Assert.False(SubscriptionRules.AllowsSync(SubscriptionState.Suspended));
        Assert.False(SubscriptionRules.AllowsSync(SubscriptionState.None));
        Assert.True(SubscriptionRules.AllowsSync(SubscriptionState.Grace));
    }

    [Fact]
    public void Renewing_early_keeps_the_remaining_time_and_renewing_late_starts_today()
    {
        Assert.Equal(Now.AddDays(10).AddMonths(12), SubscriptionRules.ExtendedExpiry(Sub(10), 12, Now));
        Assert.Equal(Now.AddMonths(12), SubscriptionRules.ExtendedExpiry(Sub(-90), 12, Now));
    }

    [Fact]
    public void Sync_health_buckets()
    {
        Assert.Equal(SyncHealth.Never, SyncHealthRules.Classify(null, Now));
        Assert.Equal(SyncHealth.Ok, SyncHealthRules.Classify(Now.AddMinutes(-5), Now));
        Assert.Equal(SyncHealth.Stale, SyncHealthRules.Classify(Now.AddHours(-3), Now));
        Assert.Equal(SyncHealth.Down, SyncHealthRules.Classify(Now.AddDays(-2), Now));
    }
}
