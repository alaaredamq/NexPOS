using Application.IServices.Sales;
using Application.Services.Sales;
using Application.Services.Users;
using Core.Auditing;
using Core.Entities.Inventory;
using Core.Entities.Sales;
using Core.Entities.Users;
using Core.Enum;
using Core.ICache;
using Infrastructure.Data;
using Infrastructure.Repositories.Products;
using Infrastructure.Repositories.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tests.Auditing;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Services;

/// <summary>
/// Changing the customer of a sale moves the order, its cart and its receipts together, and takes a sale on the
/// customer's account along with it - and a later void must take that debt off the NEW customer.
/// </summary>
public class SaleCustomerServiceTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly CollectingAuditTrail _audit = new();

    public void Dispose() => _dbFactory.Dispose();

    private CustomerDebtService Debt(ErpDbContext context) =>
        new(new CustomerDebtTransactionRepository(context, _services), new CustomerRepository(context, _services));

    private SaleCustomerService CreateSut(ErpDbContext context) =>
        new(context, Debt(context), _audit, NullLogger<SaleCustomerService>.Instance);

    private SaleReversalService CreateReversal(ErpDbContext context) =>
        new(context, new ProductRepository(context, _services), TestProductHistory.Create(context), Debt(context), Mock.Of<ICacheService>(), _audit,
            NullLogger<SaleReversalService>.Instance);

    private static async Task<int> AddCustomerAsync(ErpDbContext context)
    {
        var customer = new Customer("Second", "Customer", "second@example.test");
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        return customer.Id;
    }

    private static async Task<(Order Order, Receipt Receipt)> SeedSaleAsync(
        ErpDbContext context, int? customerId, OrderStatus status = OrderStatus.Delivered, string? snapshotName = null)
    {
        var cart = new Cart(customerId) { IsActive = false };
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var order = new Order(customerId, cart.Id, 7.5m, (int)status, 1);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var receipt = new Receipt($"RCP-TEST-{order.Id:D3}", order.Id, customerId, 1, 7.5m, 0m, 0m, 7.5m, 1, 1)
        {
            CustomerName = snapshotName,
            CustomerPhone = snapshotName == null ? null : "70123456",
        };
        context.Receipts.Add(receipt);
        await context.SaveChangesAsync();
        return (order, receipt);
    }

    [Fact]
    public async Task ChangingTheCustomer_MovesOrderCartAndReceipt_AndDropsTheOldNameSnapshot()
    {
        using var context = _dbFactory.CreateContext();
        var other = await AddCustomerAsync(context);
        var (order, receipt) = await SeedSaleAsync(context, customerId: null, snapshotName: "Old Name");

        var result = await CreateSut(context).ChangeCustomerAsync(order.Id, other, userId: 1);

        using var check = _dbFactory.CreateContext();
        Assert.Equal(other, (await check.Orders.SingleAsync(o => o.Id == order.Id)).CustomerId);
        Assert.Equal(other, (await check.Carts.SingleAsync(c => c.Id == order.CartId)).CustomerId);
        var saved = await check.Receipts.SingleAsync(r => r.Id == receipt.Id);
        Assert.Equal(other, saved.CustomerId);
        Assert.Null(saved.CustomerName);
        Assert.Null(saved.CustomerPhone);
        Assert.True(result.Changed);
        Assert.Equal(1, result.ReceiptsUpdated);
        Assert.Equal(0m, result.DebtMoved);
        Assert.Single(_audit.Entries, e => e.Action == AuditActions.SaleCustomerChanged);
    }

    [Fact]
    public async Task ACashSale_CanGoBackToWalkIn()
    {
        using var context = _dbFactory.CreateContext();
        var (order, receipt) = await SeedSaleAsync(context, customerId: 1);

        var result = await CreateSut(context).ChangeCustomerAsync(order.Id, null, 1);

        using var check = _dbFactory.CreateContext();
        Assert.Null((await check.Orders.SingleAsync(o => o.Id == order.Id)).CustomerId);
        Assert.Null((await check.Receipts.SingleAsync(r => r.Id == receipt.Id)).CustomerId);
        Assert.Null(result.CustomerId);
    }

    [Fact]
    public async Task TheSameCustomer_ChangesNothing()
    {
        using var context = _dbFactory.CreateContext();
        var (order, _) = await SeedSaleAsync(context, customerId: 1);

        var result = await CreateSut(context).ChangeCustomerAsync(order.Id, 1, 1);

        Assert.False(result.Changed);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task ADebtSale_MovesItsDebtToTheNewCustomer()
    {
        using var context = _dbFactory.CreateContext();
        var other = await AddCustomerAsync(context);
        var (order, _) = await SeedSaleAsync(context, customerId: 1);
        await Debt(context).AddSaleTransactionAsync(1, 1, order.Id, 7.5m);

        var result = await CreateSut(context).ChangeCustomerAsync(order.Id, other, 1);

        using var check = _dbFactory.CreateContext();
        Assert.Equal(0m, (await check.Customers.SingleAsync(c => c.Id == 1)).Balance);
        Assert.Equal(7.5m, (await check.Customers.SingleAsync(c => c.Id == other)).Balance);
        Assert.Equal(7.5m, result.DebtMoved);
        var transfers = await check.CustomerDebtTransactions
            .Where(t => t.OrderId == order.Id && t.Type == CustomerDebtTransactionType.SaleTransfer).ToListAsync();
        Assert.Equal(2, transfers.Count);
        Assert.Equal(0m, transfers.Sum(t => t.Amount));
    }

    [Fact]
    public async Task VoidingAMovedDebtSale_TakesTheDebtOffTheNewCustomer()
    {
        using var context = _dbFactory.CreateContext();
        var other = await AddCustomerAsync(context);
        var (order, receipt) = await SeedSaleAsync(context, customerId: 1);
        await Debt(context).AddSaleTransactionAsync(1, 1, order.Id, 7.5m);
        await CreateSut(context).ChangeCustomerAsync(order.Id, other, 1);

        var reversed = await CreateReversal(context).VoidReceiptAsync(receipt.Id, 1, "wrong sale");

        using var check = _dbFactory.CreateContext();
        Assert.Equal(7.5m, reversed.DebtReversed);
        Assert.Equal(0m, (await check.Customers.SingleAsync(c => c.Id == 1)).Balance);
        Assert.Equal(0m, (await check.Customers.SingleAsync(c => c.Id == other)).Balance);
    }

    [Fact]
    public async Task MovingTheDebtBackAndForth_KeepsTheBalancesRight()
    {
        using var context = _dbFactory.CreateContext();
        var other = await AddCustomerAsync(context);
        var (order, _) = await SeedSaleAsync(context, customerId: 1);
        await Debt(context).AddSaleTransactionAsync(1, 1, order.Id, 7.5m);
        var sut = CreateSut(context);

        await sut.ChangeCustomerAsync(order.Id, other, 1);
        await sut.ChangeCustomerAsync(order.Id, 1, 1);

        using var check = _dbFactory.CreateContext();
        Assert.Equal(7.5m, (await check.Customers.SingleAsync(c => c.Id == 1)).Balance);
        Assert.Equal(0m, (await check.Customers.SingleAsync(c => c.Id == other)).Balance);
    }

    [Fact]
    public async Task ADebtSale_CannotGoToWalkIn()
    {
        using var context = _dbFactory.CreateContext();
        var (order, _) = await SeedSaleAsync(context, customerId: 1);
        await Debt(context).AddSaleTransactionAsync(1, 1, order.Id, 7.5m);

        var ex = await Assert.ThrowsAsync<SaleCustomerException>(() => CreateSut(context).ChangeCustomerAsync(order.Id, null, 1));

        Assert.Equal(SaleCustomerFailure.DebtNeedsCustomer, ex.Failure);
        using var check = _dbFactory.CreateContext();
        Assert.Equal(1, (await check.Orders.SingleAsync(o => o.Id == order.Id)).CustomerId);
        Assert.Equal(7.5m, (await check.Customers.SingleAsync(c => c.Id == 1)).Balance);
    }

    [Fact]
    public async Task AReversedDebtSale_IsNoLongerOnAccount_SoItCanGoToWalkIn()
    {
        using var context = _dbFactory.CreateContext();
        var (order, _) = await SeedSaleAsync(context, customerId: 1, status: OrderStatus.Delivered);
        await Debt(context).AddSaleTransactionAsync(1, 1, order.Id, 7.5m);
        await Debt(context).ReverseSaleAsync(order.Id, 1);

        var result = await CreateSut(context).ChangeCustomerAsync(order.Id, null, 1);

        Assert.True(result.Changed);
        Assert.Equal(0m, result.DebtMoved);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Returned)]
    public async Task AClosedOrder_KeepsItsCustomer(OrderStatus status)
    {
        using var context = _dbFactory.CreateContext();
        var other = await AddCustomerAsync(context);
        var (order, _) = await SeedSaleAsync(context, customerId: 1, status);

        var ex = await Assert.ThrowsAsync<SaleCustomerException>(() => CreateSut(context).ChangeCustomerAsync(order.Id, other, 1));

        Assert.Equal(SaleCustomerFailure.OrderClosed, ex.Failure);
    }

    [Fact]
    public async Task AnUnknownCustomer_OrOrder_IsRefused()
    {
        using var context = _dbFactory.CreateContext();
        var (order, _) = await SeedSaleAsync(context, customerId: 1);
        var sut = CreateSut(context);

        var noCustomer = await Assert.ThrowsAsync<SaleCustomerException>(() => sut.ChangeCustomerAsync(order.Id, 9999, 1));
        var noOrder = await Assert.ThrowsAsync<SaleCustomerException>(() => sut.ChangeCustomerAsync(9999, 1, 1));

        Assert.Equal(SaleCustomerFailure.CustomerNotFound, noCustomer.Failure);
        Assert.Equal(SaleCustomerFailure.OrderNotFound, noOrder.Failure);
    }

    [Fact]
    public async Task AVoidedReceipt_IsLeftAloneWhenTheOrderIsStillOpen()
    {
        // A receipt already voided stays as it was; only the live receipts follow the new customer.
        using var context = _dbFactory.CreateContext();
        var other = await AddCustomerAsync(context);
        var (order, voided) = await SeedSaleAsync(context, customerId: 1);
        voided.VoidReceipt(1, "reissued");
        var live = new Receipt($"RCP-TEST-L{order.Id:D3}", order.Id, 1, 1, 7.5m, 0m, 0m, 7.5m, 1, 1);
        context.Receipts.Add(live);
        await context.SaveChangesAsync();

        var result = await CreateSut(context).ChangeCustomerAsync(order.Id, other, 1);

        using var check = _dbFactory.CreateContext();
        Assert.Equal(1, result.ReceiptsUpdated);
        Assert.Equal(1, (await check.Receipts.SingleAsync(r => r.Id == voided.Id)).CustomerId);
        Assert.Equal(other, (await check.Receipts.SingleAsync(r => r.Id == live.Id)).CustomerId);
    }
}
