using System.Net.Sockets;
using Microsoft.Data.Sqlite;

// Clears every sale from a NexPOS database - orders, receipts and the carts behind them - so the
// dashboard starts from zero. Products, customers, suppliers, users and settings are left alone.
//
//   ClearSales                   -> %LocalAppData%\NexPOS\nexpos.db (NexPOS must be closed)
//   ClearSales <path-to-db>      -> that file instead
//   ClearSales ... --yes         -> skip the "type YES" question

var yes = args.Contains("--yes");
var pathArg = args.FirstOrDefault(a => !a.StartsWith("--"));
var dbPath = pathArg ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexPOS", "nexpos.db");

if (!File.Exists(dbPath)) return Fail($"Database not found: {dbPath}");

// NexPOS keeps the file open while it runs; writing underneath it would corrupt the WAL.
if (pathArg is null && IsNexPosRunning())
    return Fail("NexPOS is running. Close it completely (check Task Manager for NexPos / Web), then run this again.");

using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
db.Open();

Console.WriteLine($"Database: {dbPath}");
Console.WriteLine("Currently in it:");
foreach (var t in new[] { "Orders", "Receipts", "ReceiptItems", "ReceiptPayments", "Carts", "CartItems", "CustomerCarts" })
    Console.WriteLine($"  {t,-16}{Count(db, t)}");
Console.WriteLine($"  {"Products",-16}{Count(db, "Products")}   (kept)");
Console.WriteLine($"  {"Customers",-16}{Count(db, "Customers")}   (kept)");

if (Count(db, "Orders") == 0 && Count(db, "Receipts") == 0)
    Console.WriteLine("\nThere are no orders or receipts to remove.");

if (!yes)
{
    Console.Write("\nDelete all orders and receipts? A backup is made first. Type YES to continue: ");
    if (!string.Equals(Console.ReadLine()?.Trim(), "YES", StringComparison.Ordinal)) return Fail("Cancelled. Nothing was changed.");
}

// VACUUM INTO writes a complete, consistent copy that already includes anything still sitting in the WAL.
var backup = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!,
    $"nexpos.before-clear-{DateTime.Now:yyyyMMdd-HHmmss}.db");
Exec(db, $"VACUUM INTO '{backup.Replace("'", "''")}'");
Console.WriteLine($"\nBackup saved: {backup}");

Exec(db, "PRAGMA foreign_keys = ON");
using (var tx = db.BeginTransaction())
{
    // Sale-related debt rows only (they point at an order). Manual payments/adjustments stay, so a
    // customer's real balance is only reduced by what those removed rows had added to it.
    using (var cmd = db.CreateCommand())
    {
        cmd.Transaction = tx;
        cmd.CommandText = @"UPDATE Customers SET Balance = Balance - COALESCE(
                              (SELECT SUM(Amount) FROM CustomerDebtTransactions d
                                WHERE d.CustomerId = Customers.Id AND d.OrderId IS NOT NULL), 0)
                            WHERE Id IN (SELECT CustomerId FROM CustomerDebtTransactions WHERE OrderId IS NOT NULL)";
        cmd.ExecuteNonQuery();
    }

    // Carts that belong to an order (held/parked carts that never became a sale are kept).
    Exec(db, "CREATE TEMP TABLE _sale_carts AS SELECT CartId AS Id FROM Orders", tx);
    Exec(db, "DELETE FROM CustomerDebtTransactions WHERE OrderId IS NOT NULL", tx);
    Exec(db, "DELETE FROM ReceiptPayments", tx);
    Exec(db, "DELETE FROM ReceiptItems", tx);
    Exec(db, "DELETE FROM Receipts", tx);
    Exec(db, "DELETE FROM Orders", tx);
    Exec(db, "DELETE FROM CustomerCarts WHERE CartId IN (SELECT Id FROM _sale_carts)", tx);
    Exec(db, "DELETE FROM CartItems WHERE CartId IN (SELECT Id FROM _sale_carts)", tx);
    Exec(db, "DELETE FROM Carts WHERE Id IN (SELECT Id FROM _sale_carts)", tx);
    Exec(db, "DROP TABLE _sale_carts", tx);

    // Next order/receipt is #1 again.
    Exec(db, "DELETE FROM sqlite_sequence WHERE name IN ('Orders','Receipts','ReceiptItems','ReceiptPayments')", tx);
    // A lock row left by an interrupted migration makes the next start-up wait forever.
    Exec(db, "DELETE FROM __EFMigrationsLock", tx);
    tx.Commit();
}
Exec(db, "PRAGMA wal_checkpoint(TRUNCATE)");

var bad = Count(db, "pragma_foreign_key_check");
Console.WriteLine("\nDone. Now in the database:");
foreach (var t in new[] { "Orders", "Receipts", "ReceiptItems", "ReceiptPayments", "Carts", "CartItems" })
    Console.WriteLine($"  {t,-16}{Count(db, t)}");
Console.WriteLine($"  {"Products",-16}{Count(db, "Products")}");
Console.WriteLine($"  {"Customers",-16}{Count(db, "Customers")}");
if (bad > 0) Console.WriteLine($"\nWARNING: {bad} broken reference(s) found. Restore the backup above and send it to support.");
Console.WriteLine("\nStart NexPOS - the dashboard will show 0.");
return bad > 0 ? 2 : 0;

static int Fail(string message) { Console.Error.WriteLine(message); return 1; }

static long Count(SqliteConnection db, string table)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
    return Convert.ToInt64(cmd.ExecuteScalar());
}

static void Exec(SqliteConnection db, string sql, SqliteTransaction? tx = null)
{
    using var cmd = db.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}

static bool IsNexPosRunning()
{
    try
    {
        using var client = new TcpClient();
        return client.ConnectAsync("127.0.0.1", 5000).Wait(500) && client.Connected;
    }
    catch { return false; }
}
