using Application.IServices.Users;
using Application.Services.Sales;
using Application.Services.Users;
using Core.Auditing;
using Core.Entities.Inventory;
using Core.Entities.Products;
using Core.Entities.Sales;
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
/// Voiding a receipt (or cancelling an order) must undo the whole sale, not just flip a flag: the order
/// is cancelled, the stock goes back, a debt sale comes off the customer's account, and all of it
/// happens together or not at all. Before this, a voided receipt left its order "Delivered" - still
/// counted as revenue, with the goods still missing from stock.
/// </summary>
public class SaleReversalServiceTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly CollectingAuditTrail _audit = new();
    private int _barcode;

    public void Dispose() => _dbFactory.Dispose();

    private SaleReversalService CreateSut(ErpDbContext context, ICustomerDebtService? debtService = null) =>
        new(context,
            new ProductRepository(context, _services),
            debtService ?? new CustomerDebtService(
                new CustomerDebtTransactionRepository(context, _services),
                new CustomerRepository(context, _services)),
            Mock.Of<ICacheService>(),
            _audit,
            NullLogger<SaleReversalService>.Instance);

    /// <summary>One sold line: a product (stock already reduced by the sale), its order and - optionally - receipt.</summary>
    private async Task<(Product Product, Order Order, Receipt? Receipt)> SeedSaleAsync(
        ErpDbContext context, decimal stockAfterSale = 7m, int quantity = 3, decimal? weightOrVolume = null,
        int? customerId = null, bool withReceipt = true, OrderStatus status = OrderStatus.Delivered)
    {
        var product = new Product
        {
            Name = "Test Widget", Barcode = $"REV-{++_barcode}", SellingPrice = 2.5m, StockQuantity = stockAfterSale,
            SupplierId = 1, CurrencyId = 1, CategoryId = 1, IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var cart = new Cart(customerId) { IsActive = false };
        cart.CartItems.Add(new CartItem(0, product.Id, quantity, 2.5m, weightOrVolume));
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var order = new Order(customerId, cart.Id, 7.5m, (int)status, 1);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        Receipt? receipt = null;
        if (withReceipt)
        {
            receipt = new Receipt($"RCP-TEST-{order.Id:D3}", order.Id, customerId, 1, 7.5m, 0m, 0m, 7.5m, 1, 1);
            context.Receipts.Add(receipt);
            await context.SaveChangesAsync();
        }

        return (product, order, receipt);
    }

    [Fact]
    public async Task VoidingAReceipt_CancelsTheOrder_AndReturnsTheStock()
    {
        using var context = _dbFactory.CreateContext();
        var (product, order, receipt) = await SeedSaleAsync(context, stockAfterSale: 7m, quantity: 3);

        var result = await CreateSut(context).VoidReceiptAsync(receipt!.Id, userId: 1, "Customer changed their mind");

        using var check = _dbFactory.CreateContext();
        var savedReceipt = await check.Receipts.SingleAsync(r => r.Id == receipt.Id);
        Assert.True(savedReceipt.IsVoid);
        Assert.Equal("Customer changed their mind", savedReceipt.VoidReason);
        Assert.Equal((int)OrderStatus.Cancelled, (await check.Orders.SingleAsync(o => o.Id == order.Id)).Status);
        Assert.Equal(10m, (await check.Products.SingleAsync(p => p.Id == product.Id)).StockQuantity);

        Assert.True(result.OrderCancelled);
        Assert.Equal(1, result.ReceiptsVoided);
        Assert.Equal(1, result.StockLinesRestored);
    }

    [Fact]
    public async Task AWeighedLine_ReturnsTheWeight_NotTheMeaninglessCartQuantity()
    {
        using var context = _dbFactory.CreateContext();
        // 0.3 kg sold: the cart row's Quantity is junk for weighed goods, WeightOrVolume is what was deducted.
        var (product, _, receipt) = await SeedSaleAsync(context, stockAfterSale: 4.7m, quantity: 300, weightOrVolume: 0.3m);

        await CreateSut(context).VoidReceiptAsync(receipt!.Id, 1, "mistake");

        using var check = _dbFactory.CreateContext();
        Assert.Equal(5.0m, (await check.Products.SingleAsync(p => p.Id == product.Id)).StockQuantity);
    }

    [Fact]
    public async Task AnOrderWithNoReceipt_CanStillBeCancelled()
    {
        // The orders that lost their receipt to the after-midnight numbering bug.
        using var context = _dbFactory.CreateContext();
        var (product, order, _) = await SeedSaleAsync(context, withReceipt: false);

        var result = await CreateSut(context).CancelOrderAsync(order.Id, 1, "no receipt");

        using var check = _dbFactory.CreateContext();
        Assert.Equal((int)OrderStatus.Cancelled, (await check.Orders.SingleAsync(o => o.Id == order.Id)).Status);
        Assert.Equal(10m, (await check.Products.SingleAsync(p => p.Id == product.Id)).StockQuantity);
        Assert.Equal(0, result.ReceiptsVoided);
    }

    [Fact]
    public async Task CancellingAnOrder_AlsoVoidsItsReceipt()
    {
        using var context = _dbFactory.CreateContext();
        var (_, order, receipt) = await SeedSaleAsync(context);

        var result = await CreateSut(context).CancelOrderAsync(order.Id, 1, "cancelled");

        using var check = _dbFactory.CreateContext();
        Assert.True((await check.Receipts.SingleAsync(r => r.Id == receipt!.Id)).IsVoid);
        Assert.Equal(1, result.ReceiptsVoided);
    }

    [Fact]
    public async Task ADebtSale_ComesOffTheCustomersAccount_Once()
    {
        using var context = _dbFactory.CreateContext();
        var debt = new CustomerDebtService(
            new CustomerDebtTransactionRepository(context, _services), new CustomerRepository(context, _services));
        var (_, order, receipt) = await SeedSaleAsync(context, customerId: 1);
        await debt.AddSaleTransactionAsync(customerId: 1, userId: 1, order.Id, 7.5m);
        Assert.Equal(7.5m, (await context.Customers.SingleAsync(c => c.Id == 1)).Balance);

        var result = await CreateSut(context, debt).VoidReceiptAsync(receipt!.Id, 1, "wrong customer");

        using var check = _dbFactory.CreateContext();
        Assert.Equal(0m, (await check.Customers.SingleAsync(c => c.Id == 1)).Balance);
        var ledger = await check.CustomerDebtTransactions.Where(t => t.OrderId == order.Id).ToListAsync();
        Assert.Contains(ledger, t => t.Type == CustomerDebtTransactionType.SaleReversal && t.Amount == -7.5m);
        Assert.Equal(7.5m, result.DebtReversed);

        // Asking again must not take the money off a second time.
        Assert.Equal(0m, await debt.ReverseSaleAsync(order.Id, 1));
        Assert.Equal(0m, (await check.Customers.AsNoTracking().SingleAsync(c => c.Id == 1)).Balance);
    }

    [Fact]
    public async Task ACashSale_LeavesTheCustomerBalanceAlone()
    {
        using var context = _dbFactory.CreateContext();
        var (_, _, receipt) = await SeedSaleAsync(context, customerId: 1);

        var result = await CreateSut(context).VoidReceiptAsync(receipt!.Id, 1, "refund");

        using var check = _dbFactory.CreateContext();
        Assert.Equal(0m, (await check.Customers.SingleAsync(c => c.Id == 1)).Balance);
        Assert.Equal(0m, result.DebtReversed);
    }

    [Fact]
    public async Task VoidingTwice_IsRefused()
    {
        using var context = _dbFactory.CreateContext();
        var (_, _, receipt) = await SeedSaleAsync(context);
        var sut = CreateSut(context);
        await sut.VoidReceiptAsync(receipt!.Id, 1, "first");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.VoidReceiptAsync(receipt.Id, 1, "second"));
    }

    [Fact]
    public async Task CancellingAnAlreadyCancelledOrder_IsRefused_AndDoesNotRestockAgain()
    {
        using var context = _dbFactory.CreateContext();
        var (product, order, _) = await SeedSaleAsync(context);
        var sut = CreateSut(context);
        await sut.CancelOrderAsync(order.Id, 1, "first");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CancelOrderAsync(order.Id, 1, "again"));

        using var check = _dbFactory.CreateContext();
        Assert.Equal(10m, (await check.Products.SingleAsync(p => p.Id == product.Id)).StockQuantity);
    }

    [Fact]
    public async Task VoidingTheReceiptOfAnOrderAlreadyCancelled_OnlyVoidsTheReceipt()
    {
        // e.g. an order someone cancelled by hand before this existed: its stock was never returned and must not be guessed at.
        using var context = _dbFactory.CreateContext();
        var (product, _, receipt) = await SeedSaleAsync(context, stockAfterSale: 7m, status: OrderStatus.Cancelled);

        var result = await CreateSut(context).VoidReceiptAsync(receipt!.Id, 1, "tidy up");

        using var check = _dbFactory.CreateContext();
        Assert.True((await check.Receipts.SingleAsync(r => r.Id == receipt.Id)).IsVoid);
        Assert.Equal(7m, (await check.Products.SingleAsync(p => p.Id == product.Id)).StockQuantity);
        Assert.False(result.OrderCancelled);
    }

    [Fact]
    public async Task IfAnyStepFails_NothingIsChanged()
    {
        using var context = _dbFactory.CreateContext();
        var (product, order, receipt) = await SeedSaleAsync(context, customerId: 1);
        var failingDebt = new Mock<ICustomerDebtService>();
        failingDebt.Setup(d => d.ReverseSaleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("ledger unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut(context, failingDebt.Object).VoidReceiptAsync(receipt!.Id, 1, "boom"));

        // The stock had already been put back inside the transaction when the debt step blew up - it must be rolled back too.
        using var check = _dbFactory.CreateContext();
        Assert.False((await check.Receipts.SingleAsync(r => r.Id == receipt.Id)).IsVoid);
        Assert.Equal((int)OrderStatus.Delivered, (await check.Orders.SingleAsync(o => o.Id == order.Id)).Status);
        Assert.Equal(7m, (await check.Products.SingleAsync(p => p.Id == product.Id)).StockQuantity);
    }

    [Fact]
    public async Task TheReversal_IsWrittenToTheAuditTrail()
    {
        using var context = _dbFactory.CreateContext();
        var (_, order, receipt) = await SeedSaleAsync(context);
        var sut = CreateSut(context);

        await sut.VoidReceiptAsync(receipt!.Id, 1, "audit me");

        var entry = Assert.Single(_audit.Entries, e => e.Action == AuditActions.ReceiptVoided);
        Assert.Equal(receipt.ReceiptNumber, entry.EntityLabel);
        Assert.Contains("audit me", entry.AdditionalInfoJson!);

        var (_, order2, _) = await SeedSaleAsync(context, withReceipt: false);
        await sut.CancelOrderAsync(order2.Id, 1, "cancel me");
        Assert.Single(_audit.Entries, e => e.Action == AuditActions.OrderCancelled);
    }
}

/// <summary>Setting an order's status by hand must not be a back door around the reversal.</summary>
public class OrderStatusGuardTests
{
    private static OrderService CreateSut(Order order)
    {
        var orders = new Mock<Core.IRepositories.Sales.IOrderRepository>();
        orders.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);
        orders.Setup(r => r.SaveAsync(It.IsAny<Order>())).ReturnsAsync((Order o) => o);
        return new OrderService(orders.Object,
            Mock.Of<Core.IRepositories.Inventory.ICartRepository>(),
            Mock.Of<Core.IRepositories.Inventory.ICartItemRepository>(),
            Mock.Of<Core.IRepositories.Products.IProductRepository>(),
            Mock.Of<Application.IServices.Notifications.IStockAlertService>(),
            Mock.Of<Application.IServices.Licensing.ILicenseService>(),
            NullLogger<OrderService>.Instance);
    }

    private static Order OrderWith(OrderStatus status) =>
        new(customerId: null, cartId: 1, totalAmount: 5m, (int)status, currencyId: 1) { Id = 1 };

    [Fact]
    public async Task SettingStatusToCancelled_IsRefused_ItMustGoThroughTheReversal()
    {
        var sut = CreateSut(OrderWith(OrderStatus.Delivered));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.UpdateOrderStatusAsync(1, (int)OrderStatus.Cancelled));
    }

    [Fact]
    public async Task ACancelledOrder_CannotBeReopened()
    {
        var sut = CreateSut(OrderWith(OrderStatus.Cancelled));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.UpdateOrderStatusAsync(1, (int)OrderStatus.Delivered));
    }

    [Fact]
    public async Task OtherStatusChanges_StillWork()
    {
        var order = OrderWith(OrderStatus.Pending);

        await CreateSut(order).UpdateOrderStatusAsync(1, (int)OrderStatus.Delivered);

        Assert.Equal((int)OrderStatus.Delivered, order.Status);
    }
}
