using Application.IServices.Base;
using Application.IServices.Inventory;
using Application.Services.Inventory;
using Application.Services.Products;
using Core.Entities.Inventory;
using Core.Entities.Products;
using Core.IRepositories.Inventory;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Services;

/// <summary>
/// The till lets a cashier pick one of a product's stored prices or type a custom one. The browser never
/// sets the price (S2): the server resolves it, and a custom price below the purchase price is refused.
/// Seeded currencies: 1 = USD (rate 1), 3 = LBP (rate 89,500).
/// </summary>
public class CartServicePriceTests : IDisposable
{
    private const int Usd = 1;
    private const int Lbp = 3;

    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();

    private static Product NewProduct(string barcode, decimal selling, decimal? cost, int currencyId = Usd) => new()
    {
        Name = $"Product {barcode}",
        Barcode = barcode,
        SellingPrice = selling,
        CostPrice = cost,
        StockQuantity = 100,
        SupplierId = 1,
        CurrencyId = currencyId,
        CategoryId = 1,
        IsActive = true
    };

    private async Task<(CartService Sut, ErpDbContext Context, int CartId)> ArrangeAsync(params Product[] products)
    {
        var context = _dbFactory.CreateContext();
        context.Products.AddRange(products);
        var cart = new Cart(null);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var cartItemRepo = new Mock<ICartItemRepository>();
        cartItemRepo.Setup(r => r.GetByCartIdAsync(cart.Id)).ReturnsAsync(new List<CartItem>());
        var cartRepo = new Mock<ICartRepository>();
        cartRepo.Setup(r => r.GetByIdAsync(cart.Id)).ReturnsAsync(cart);

        var sut = new CartService(cartRepo.Object, cartItemRepo.Object, Mock.Of<ICustomerCartRepository>(),
            Mock.Of<ICurrencyConversionService>(), context);
        return (sut, context, cart.Id);
    }

    private static Task<decimal> SavedUnitPrice(ErpDbContext context, int productId) =>
        context.Set<CartItem>().Where(i => i.ProductId == productId).Select(i => i.UnitPrice).SingleAsync();

    [Fact]
    public async Task NoPriceChosen_UsesTheProductSellingPrice()
    {
        var p = NewProduct("PRICE-001", selling: 25m, cost: 10m);
        var (sut, context, cartId) = await ArrangeAsync(p);
        using var _ = context;

        await sut.AddItemsToCartAsync(cartId, new[] { new CartItemInput(p.Id, 1) });

        Assert.Equal(25m, await SavedUnitPrice(context, p.Id));
    }

    [Fact]
    public async Task StoredPriceChosen_IsChargedAtThatPrice()
    {
        var p = NewProduct("PRICE-002", selling: 25m, cost: 10m);
        var (sut, context, cartId) = await ArrangeAsync(p);
        using var _ = context;
        var wholesale = new ProductPrice { ProductId = p.Id, Name = "Wholesale", Price = 21m };
        context.ProductPrices.Add(wholesale);
        await context.SaveChangesAsync();

        await sut.AddItemsToCartAsync(cartId, new[] { new CartItemInput(p.Id, 2, PriceTierId: wholesale.Id) });

        Assert.Equal(21m, await SavedUnitPrice(context, p.Id));
    }

    [Fact]
    public async Task StoredPriceOfAnotherProduct_IsRefused()
    {
        var a = NewProduct("PRICE-003A", selling: 25m, cost: 10m);
        var b = NewProduct("PRICE-003B", selling: 50m, cost: 20m);
        var (sut, context, cartId) = await ArrangeAsync(a, b);
        using var _ = context;
        var bsWholesale = new ProductPrice { ProductId = b.Id, Name = "Wholesale", Price = 1m };
        context.ProductPrices.Add(bsWholesale);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.AddItemsToCartAsync(cartId, new[] { new CartItemInput(a.Id, 1, PriceTierId: bsWholesale.Id) }));
        Assert.Empty(context.Set<CartItem>());
    }

    [Theory]
    [InlineData("10.00")]   // exactly the purchase price is allowed
    [InlineData("10.01")]
    [InlineData("500")]
    public async Task CustomPriceAtOrAbovePurchasePrice_IsAccepted(string price)
    {
        var p = NewProduct("PRICE-004", selling: 25m, cost: 10m);
        var (sut, context, cartId) = await ArrangeAsync(p);
        using var _ = context;
        var custom = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        await sut.AddItemsToCartAsync(cartId, new[] { new CartItemInput(p.Id, 1, CustomUnitPrice: custom, CustomPriceCurrencyId: Usd) });

        Assert.Equal(custom, await SavedUnitPrice(context, p.Id));
    }

    [Theory]
    [InlineData("9.99")]
    [InlineData("0.01")]
    public async Task CustomPriceBelowPurchasePrice_IsRefusedAndNothingIsSaved(string price)
    {
        var p = NewProduct("PRICE-005", selling: 25m, cost: 10m);
        var (sut, context, cartId) = await ArrangeAsync(p);
        using var _ = context;
        var custom = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.AddItemsToCartAsync(cartId, new[] { new CartItemInput(p.Id, 1, CustomUnitPrice: custom, CustomPriceCurrencyId: Usd) }));

        Assert.Contains("below the minimum", ex.Message);
        Assert.Empty(context.Set<CartItem>());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task CustomPriceZeroOrNegative_IsRefused_EvenWithoutAPurchasePrice(string price)
    {
        var p = NewProduct("PRICE-006", selling: 25m, cost: null);
        var (sut, context, cartId) = await ArrangeAsync(p);
        using var _ = context;
        var custom = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.AddItemsToCartAsync(cartId, new[] { new CartItemInput(p.Id, 1, CustomUnitPrice: custom) }));
    }

    [Fact]
    public async Task CustomPriceWithNoPurchasePriceOnFile_AcceptsAnyPositivePrice()
    {
        var p = NewProduct("PRICE-007", selling: 25m, cost: null);
        var (sut, context, cartId) = await ArrangeAsync(p);
        using var _ = context;

        await sut.AddItemsToCartAsync(cartId, new[] { new CartItemInput(p.Id, 1, CustomUnitPrice: 0.5m, CustomPriceCurrencyId: Usd) });

        Assert.Equal(0.5m, await SavedUnitPrice(context, p.Id));
    }

    [Fact]
    public async Task CustomPriceTypedInAnotherCurrency_IsCheckedThereAndStoredInTheProductCurrency()
    {
        // Cost 0.44 USD = 39,380 LBP at 89,500. The cart shows LBP, so the cashier types LBP.
        var p = NewProduct("PRICE-008", selling: 0.60m, cost: 0.44m, currencyId: Usd);
        var (sut, context, cartId) = await ArrangeAsync(p);
        using var _ = context;

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.AddItemsToCartAsync(cartId, new[] { new CartItemInput(p.Id, 1, CustomUnitPrice: 39379m, CustomPriceCurrencyId: Lbp) }));

        await sut.AddItemsToCartAsync(cartId, new[] { new CartItemInput(p.Id, 1, CustomUnitPrice: 39380m, CustomPriceCurrencyId: Lbp) });

        Assert.Equal(0.44m, await SavedUnitPrice(context, p.Id)); // 39,380 / 89,500, stored in USD
    }

    [Fact]
    public void PriceRules_MinimumRoundsUpSoTheDisplayedFloorIsNeverLowerThanTheRealOne()
    {
        // 0.123 USD * 89,500 = 11,008.5 LBP -> 11,008.50 (2 decimals), never 11,008.
        var product = NewProduct("PRICE-009", selling: 1m, cost: 0.123m);
        var usd = new Core.Entities.Sales.Currency("US Dollar", 1f, true, true, "USD", "$") { Id = Usd };
        var lbp = new Core.Entities.Sales.Currency("Lebanese Pound", 89500f, false, true, "LBP", "LBP") { Id = Lbp };

        Assert.Equal(11008.50m, PriceRules.MinimumUnitPrice(product, usd, lbp));
        Assert.Equal(0.13m, PriceRules.MinimumUnitPrice(product, usd, usd));
    }

    public void Dispose() => _dbFactory.Dispose();
}
