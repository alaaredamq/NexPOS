using Application.DTOs.Sales;
using Application.IServices.Inventory;
using Application.IServices.Licensing;
using Application.IServices.Sales;
using Application.IServices.Users;
using Application.Services.Sales;
using Core.Discounts;
using Core.Entities.Inventory;
using Core.Entities.Products;
using Core.Entities.Sales;
using Core.ICache;
using Core.IRepositories.Inventory;
using Core.IRepositories.Sales;
using Infrastructure.Repositories.PrinterTransport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tests.Auditing;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Services;

/// <summary>
/// A discount is typed as a percentage OR a fixed amount, but every place that shows it (the POS, the
/// receipt, the order pages, the Z-report) needs both forms. A cart only stores the typed one, so a
/// percentage discount used to reach the receipt - and the Z-report - as no discount at all.
/// </summary>
public class DiscountDisplayTests
{
    // ---- DiscountBreakdown ----

    [Fact]
    public void From_Percentage_GivesTheMoneyOff()
    {
        var discount = DiscountBreakdown.From(subtotal: 200m, amount: null, percentage: 10m);

        Assert.Equal(20m, discount.Amount);
        Assert.Equal(10m, discount.Percentage);
    }

    [Fact]
    public void From_FixedAmount_GivesThePercentage()
    {
        var discount = DiscountBreakdown.From(subtotal: 200m, amount: 30m, percentage: null);

        Assert.Equal(30m, discount.Amount);
        Assert.Equal(15m, discount.Percentage);
    }

    [Fact]
    public void From_PercentageWinsWhenBothAreSet()
    {
        // Same precedence checkout uses when it totals the cart.
        var discount = DiscountBreakdown.From(subtotal: 200m, amount: 50m, percentage: 10m);

        Assert.Equal(20m, discount.Amount);
        Assert.Equal(10m, discount.Percentage);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(-5, -5)]
    public void From_NothingUsable_IsNoDiscount(int? amount, int? percentage)
    {
        var discount = DiscountBreakdown.From(200m, (decimal?)amount, (decimal?)percentage);

        Assert.Equal(DiscountBreakdown.None, discount);
        Assert.False(discount.HasDiscount);
    }

    [Fact]
    public void From_FixedAmountOnEmptySubtotal_DoesNotDivideByZero()
    {
        var discount = DiscountBreakdown.From(subtotal: 0m, amount: 5m, percentage: null);

        Assert.Equal(5m, discount.Amount);
        Assert.Equal(0m, discount.Percentage);
    }

    [Theory]
    [InlineData("10", "10%")]
    [InlineData("12.5", "12.5%")]
    [InlineData("15.1515", "15.15%")]
    [InlineData("33.3333333", "33.33%")]
    public void FormatPercentage_ShowsAtMostTwoDecimals(string value, string expected)
    {
        Assert.Equal(expected, DiscountBreakdown.FormatPercentage(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    // ---- Receipt ----

    private static Receipt NewReceipt(decimal subTotal, decimal discount, decimal total, decimal tax = 0m) =>
        new("RCP-1", orderId: 1, customerId: null, cashierId: 1, subTotal, tax, discount, total, paymentMethodId: 1, currencyId: 1);

    [Fact]
    public void Receipt_GetDiscount_DerivesThePercentageFromTheStoredAmount()
    {
        // Only the money is stored, so a typed 10% and a typed $20 on the same sale read the same.
        var discount = NewReceipt(subTotal: 200m, discount: 20m, total: 180m).GetDiscount();

        Assert.Equal(20m, discount.Amount);
        Assert.Equal("10%", discount.PercentageText);
    }

    [Fact]
    public void Receipt_GetAdjustment_IsZeroWhenTheLinesAddUp()
    {
        Assert.Equal(0m, NewReceipt(200m, 20m, 180m).GetAdjustment());
        Assert.Equal(0m, NewReceipt(200m, 20m, 190m, tax: 10m).GetAdjustment());
    }

    [Fact]
    public void Receipt_GetAdjustment_IgnoresSubCentRounding()
    {
        // 3.33 less 10% is 2.997; the POS total field rounds that to 3.00.
        Assert.Equal(0m, NewReceipt(3.33m, 0.333m, 3.00m).GetAdjustment());
    }

    [Fact]
    public void Receipt_GetAdjustment_ReportsWhatTheCashierChangedTheTotalBy()
    {
        Assert.Equal(-0.25m, NewReceipt(200m, 20m, 179.75m).GetAdjustment());
        Assert.Equal(0.50m, NewReceipt(200m, 20m, 180.50m).GetAdjustment());
    }

    // ---- Printed receipt ----

    [Fact]
    public void ReceiptText_ShowsTheDiscountAsMoneyAndAsAPercentage()
    {
        var text = new EscPosBuilder().BuildPreviewText(NewReceipt(subTotal: 200m, discount: 30m, total: 170m));

        Assert.Contains("Subtotal:", text);
        Assert.Contains("200.00", text);
        Assert.Contains("Discount (15%):", text);
        Assert.Contains("-30.00", text);
        Assert.Contains("170.00", text);
        Assert.DoesNotContain("Adjustment", text);
    }

    [Fact]
    public void ReceiptText_ShowsAnAdjustmentSoTheLinesAddUp()
    {
        var text = new EscPosBuilder().BuildPreviewText(NewReceipt(subTotal: 200m, discount: 20m, total: 179.75m));

        Assert.Contains("Discount (10%):", text);
        Assert.Contains("Adjustment:", text);
        Assert.Contains("-0.25", text);
    }

    // ---- Checkout -> receipt ----

    /// <summary>Checks out a 2 x 100 cart carrying the given discount and returns what the receipt was created from.</summary>
    private static async Task<CreateReceiptRequest> CheckoutAsync(decimal? discountAmount, decimal? discountPercentage, decimal requestedTotal)
    {
        using var db = new SqliteInMemoryDbContextFactory();
        using var context = db.CreateContext();

        var cart = new Cart(customerId: null) { Id = 1, DiscountAmount = discountAmount, DiscountPercentage = discountPercentage };
        cart.CartItems.Add(new CartItem(1, productId: 1, quantity: 2, unitPrice: 100m)
        {
            Product = new Product { Name = "Widget", Barcode = "D-1" }
        });

        var cartRepository = new Mock<ICartRepository>();
        cartRepository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(cart);

        var cash = new PaymentMethod("Cash", isDefault: true) { Id = 1 };
        var paymentMethods = new Mock<IPaymentMethodService>();
        paymentMethods.Setup(s => s.GetAllAsync()).ReturnsAsync([cash]);
        paymentMethods.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(cash);

        var orders = new Mock<IOrderService>();
        orders.Setup(s => s.CreateOrderFromCartAsync(
                It.IsAny<Cart>(), It.IsAny<IReadOnlyList<CartItem>>(), It.IsAny<int?>(), It.IsAny<decimal>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool>()))
            .ReturnsAsync(new Order(null, 1, requestedTotal) { Id = 5 });

        CreateReceiptRequest? captured = null;
        var receipts = new Mock<IReceiptService>();
        receipts.Setup(s => s.CreateReceiptAsync(It.IsAny<CreateReceiptRequest>()))
            .Callback<CreateReceiptRequest>(r => captured = r)
            .ReturnsAsync(NewReceipt(200m, 0m, requestedTotal));

        var idempotency = new Mock<ICheckoutIdempotencyGuard>();
        idempotency.Setup(g => g.TryBeginCheckout(It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns(true);

        var sut = new POSCheckoutService(
            orders.Object, Mock.Of<ICartService>(), paymentMethods.Object, Mock.Of<IDiscountService>(),
            cartRepository.Object, Mock.Of<ICartItemRepository>(), Mock.Of<ICashDrawerService>(), receipts.Object,
            Mock.Of<IThermalPrinterService>(), idempotency.Object, Mock.Of<ICustomerDebtService>(),
            Mock.Of<ICacheService>(), context, Mock.Of<ILicenseService>(),
            NullLogger<POSCheckoutService>.Instance, new CollectingAuditTrail());

        await sut.ProcessCheckoutAsync(new CheckoutRequest
        {
            CartId = 1, UserId = 1, PaymentMethodId = 1, CurrencyId = 1,
            TotalAmount = requestedTotal, PrintReceipt = false
        });

        return captured ?? throw new InvalidOperationException("Checkout did not create a receipt.");
    }

    [Fact]
    public async Task Checkout_PercentageDiscount_ReachesTheReceiptAsMoney()
    {
        // Before: the receipt got DiscountAmount 0 and a "subtotal" equal to the total - no discount line at all.
        var receipt = await CheckoutAsync(discountAmount: null, discountPercentage: 10m, requestedTotal: 180m);

        Assert.Equal(200m, receipt.SubTotal);
        Assert.Equal(20m, receipt.DiscountAmount);
        Assert.Equal(180m, receipt.TotalAmount);
    }

    [Fact]
    public async Task Checkout_FixedAmountDiscount_ReachesTheReceiptWithTheRealSubtotal()
    {
        var receipt = await CheckoutAsync(discountAmount: 30m, discountPercentage: null, requestedTotal: 170m);

        Assert.Equal(200m, receipt.SubTotal);
        Assert.Equal(30m, receipt.DiscountAmount);
        Assert.Equal(170m, receipt.TotalAmount);
    }

    [Fact]
    public async Task Checkout_CashierLowersTheTotal_IsRecordedAsADiscountOnTopOfTheCartDiscount()
    {
        // Lowering the total at the till is a discount: it joins the cart's 10% (20.00) as money, so the
        // receipt prints one discount line with its percentage and no separate "Adjustment" line.
        var receipt = await CheckoutAsync(discountAmount: null, discountPercentage: 10m, requestedTotal: 179.75m);

        Assert.Equal(200m, receipt.SubTotal);
        Assert.Equal(20.25m, receipt.DiscountAmount);
        Assert.Equal(179.75m, receipt.TotalAmount);
    }

    [Fact]
    public async Task Checkout_CashierLowersTheTotalByMoreThanRounding_IsAcceptedAndShowsItsPercentage()
    {
        // Used to fail with "differs from the calculated total by more than the allowed rounding adjustment".
        var receipt = await CheckoutAsync(discountAmount: null, discountPercentage: null, requestedTotal: 184m);

        Assert.Equal(200m, receipt.SubTotal);
        Assert.Equal(16m, receipt.DiscountAmount);
        Assert.Equal(184m, receipt.TotalAmount);
        Assert.Equal("8%", NewReceipt(receipt.SubTotal, receipt.DiscountAmount, receipt.TotalAmount)
            .GetDiscount().PercentageText);
    }

    [Fact]
    public async Task Checkout_CashierRaisesTheTotalSlightly_StaysAnAdjustmentNotADiscount()
    {
        var receipt = await CheckoutAsync(discountAmount: null, discountPercentage: null, requestedTotal: 200.50m);

        Assert.Equal(0m, receipt.DiscountAmount);
        Assert.Equal(200.50m, receipt.TotalAmount);
    }

    [Fact]
    public async Task Checkout_NoDiscount_LeavesTheReceiptUndiscounted()
    {
        var receipt = await CheckoutAsync(discountAmount: null, discountPercentage: null, requestedTotal: 200m);

        Assert.Equal(200m, receipt.SubTotal);
        Assert.Equal(0m, receipt.DiscountAmount);
    }

    // ---- Z-report ----

    [Fact]
    public async Task SessionSummary_CountsPercentageDiscounts_AndDoesNotMistakeThemForAdjustments()
    {
        var cart = new Cart(customerId: null) { Id = 1, DiscountPercentage = 10m };
        cart.CartItems.Add(new CartItem(1, productId: 1, quantity: 2, unitPrice: 100m)); // 200
        var session = new CashDrawerSession(userId: 1, openingCash: 0m) { Id = 1 };
        session.Orders.Add(new Order(null, 1, 180m) { Cart = cart });

        var sessionRepo = new Mock<ICashDrawerSessionRepository>();
        sessionRepo.Setup(r => r.GetSessionWithDetailsAsync(1)).ReturnsAsync(session);
        var cashDropRepo = new Mock<ICashDropRepository>();
        cashDropRepo.Setup(r => r.GetTotalDropsBySessionIdAsync(1)).ReturnsAsync(0m);
        var sut = new CashDrawerService(sessionRepo.Object, cashDropRepo.Object, Mock.Of<IOrderRepository>(),
            NullLogger<CashDrawerService>.Instance);

        var summary = await sut.GetSessionSummaryAsync(1);

        Assert.Equal(20m, summary.TotalDiscounts);
        Assert.Equal(0m, summary.TotalAdjustments);
    }
}
