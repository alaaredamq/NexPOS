using Core.Entities.Sales;
using Infrastructure.Repositories.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Repositories;

/// <summary>
/// Receipt numbers ("RCP-yyyyMMdd-NNN") carry the LOCAL date but receipts store their date in UTC. The generator
/// used to look for "today's receipts" by date window, so in the hours after local midnight (00:00-03:00 in UTC+3)
/// it did not see the receipts already issued that day, handed out the same number again, and the insert failed on
/// the unique index - the sale then went through with no receipt. It must go by the numbers themselves.
/// </summary>
public class ReceiptNumberTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 24); // a *local* date
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    public void Dispose() => _dbFactory.Dispose();

    private async Task<ReceiptRepository> RepoWith(params (string Number, DateTime StoredUtc, bool Deleted, bool Void)[] existing)
    {
        var context = _dbFactory.CreateContext();
        foreach (var (number, storedUtc, deleted, isVoid) in existing)
        {
            // order/cashier/payment-method/currency ids come from the seed data HasData creates
            context.Receipts.Add(new Receipt(number, orderId: 0, customerId: null, cashierId: 1, 1m, 0m, 0m, 1m, paymentMethodId: 1, currencyId: 1)
            {
                ReceiptDate = storedUtc, IsDeleted = deleted, IsVoid = isVoid
            });
        }

        if (existing.Length > 0)
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;"); // these tests are about numbering, not orders
            await context.SaveChangesAsync();
        }

        return new ReceiptRepository(context, _services);
    }

    private static readonly DateTime EarlierToday = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NoReceiptsYet_StartsAtOne()
    {
        var repo = await RepoWith();
        Assert.Equal("RCP-20261024-001", await repo.GenerateNextReceiptNumberAsync(Day));
    }

    [Fact]
    public async Task ContinuesTheDaysSequence()
    {
        var repo = await RepoWith(
            ("RCP-20261024-001", EarlierToday, false, false),
            ("RCP-20261024-002", EarlierToday, false, false),
            ("RCP-20261024-003", EarlierToday, false, false));

        Assert.Equal("RCP-20261024-004", await repo.GenerateNextReceiptNumberAsync(Day));
    }

    [Fact]
    public async Task ReceiptStoredAsUtcOnThePreviousDay_IsStillCounted_TheBugThatLostASale()
    {
        // Issued at 00:23 local (UTC+3) on 24 Oct => stored ReceiptDate is 21:23 UTC on 23 Oct. The old date-window
        // lookup missed it (it is before local midnight when read as a local time) and offered 001 a second time.
        var repo = await RepoWith(("RCP-20261024-001", new DateTime(2026, 10, 23, 21, 23, 30, DateTimeKind.Utc), false, false));

        Assert.Equal("RCP-20261024-002", await repo.GenerateNextReceiptNumberAsync(Day));
    }

    [Fact]
    public async Task ReceiptStoredAsUtcOnTheNextDay_IsStillCounted_WestOfGreenwich()
    {
        // The mirror image: in UTC-5, 20:00 local on 24 Oct is 01:00 UTC on 25 Oct.
        var repo = await RepoWith(("RCP-20261024-001", new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc), false, false));

        Assert.Equal("RCP-20261024-002", await repo.GenerateNextReceiptNumberAsync(Day));
    }

    [Fact]
    public async Task SoftDeletedAndVoidReceiptsKeepTheirNumbers()
    {
        // The unique index covers every row, so a deleted/void receipt still occupies its number. The deleted one is
        // deliberately the highest: skipping deleted rows then hands out 003 again.
        var repo = await RepoWith(
            ("RCP-20261024-001", EarlierToday, Deleted: false, Void: false),
            ("RCP-20261024-002", EarlierToday, Deleted: false, Void: true),
            ("RCP-20261024-003", EarlierToday, Deleted: true, Void: false));

        Assert.Equal("RCP-20261024-004", await repo.GenerateNextReceiptNumberAsync(Day));
    }

    [Fact]
    public async Task PastNineHundredNinetyNine_DoesNotRepeat()
    {
        // As text "999" sorts after "1000", so "highest number as a string" restarts at 1000 forever.
        var repo = await RepoWith(
            ("RCP-20261024-998", EarlierToday, false, false),
            ("RCP-20261024-999", EarlierToday, false, false),
            ("RCP-20261024-1000", EarlierToday, false, false));

        Assert.Equal("RCP-20261024-1001", await repo.GenerateNextReceiptNumberAsync(Day));
    }

    [Fact]
    public async Task OtherDaysNumbers_AreIgnored()
    {
        var repo = await RepoWith(
            ("RCP-20261023-007", EarlierToday, false, false),
            ("RCP-20261025-003", EarlierToday, false, false));

        Assert.Equal("RCP-20261024-001", await repo.GenerateNextReceiptNumberAsync(Day));
    }

    [Fact]
    public async Task AMalformedNumberWithTheSamePrefix_DoesNotBreakNumbering()
    {
        var repo = await RepoWith(
            ("RCP-20261024-002", EarlierToday, false, false),
            ("RCP-20261024-MANUAL", EarlierToday, false, false));

        Assert.Equal("RCP-20261024-003", await repo.GenerateNextReceiptNumberAsync(Day));
    }

    [Fact]
    public async Task ConsecutiveCalls_ThroughTheRealSaveFlow_NeverCollide()
    {
        // The user's scenario end to end: keep issuing receipts "just after midnight" and saving them.
        var context = _dbFactory.CreateContext();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        var repo = new ReceiptRepository(context, _services);

        var issued = new List<string>();
        for (var i = 0; i < 25; i++)
        {
            var number = await repo.GenerateNextReceiptNumberAsync(Day);
            context.Receipts.Add(new Receipt(number, orderId: 0, null, 1, 1m, 0m, 0m, 1m, 1, 1)
            {
                ReceiptDate = new DateTime(2026, 10, 23, 21, 23, 0, DateTimeKind.Utc).AddSeconds(i) // previous UTC day
            });
            await context.SaveChangesAsync(); // would throw "UNIQUE constraint failed: Receipts.ReceiptNumber" on a repeat
            issued.Add(number);
        }

        Assert.Equal(25, issued.Distinct().Count());
        Assert.Equal("RCP-20261024-025", issued[^1]);
    }
}
