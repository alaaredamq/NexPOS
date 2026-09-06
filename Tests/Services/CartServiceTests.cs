using Application.IServices.Base;
using Application.Services.Inventory;
using Core.Entities.Inventory;
using Core.Entities.Products;
using Core.IRepositories.Inventory;
using Moq;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Services;

/// <summary>
/// Covers S2: a caller-supplied unit price must never be trusted - AddItemToCartAsync has to
/// always reprice from Product.SellingPrice, regardless of what price the caller passes in.
/// </summary>
public class CartServiceTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();

    [Fact]
    public async Task AddItemToCartAsync_IgnoresCallerSuppliedPrice_UsesProductSellingPrice()
    {
        const decimal realPrice = 25.00m;
        const decimal attackerSuppliedPrice = 0.01m;

        using var context = _dbFactory.CreateContext();
        var product = new Product
        {
            Name = "Test Widget",
            Barcode = "TEST-001",
            SellingPrice = realPrice,
            StockQuantity = 100,
            SupplierId = 1,
            CurrencyId = 1,
            CategoryId = 1,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var cartRepo = new Mock<ICartRepository>();
        var cartItemRepo = new Mock<ICartItemRepository>();
        CartItem? savedItem = null;

        cartItemRepo
            .Setup(r => r.GetByCartAndProductAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((CartItem?)null);
        cartItemRepo
            .Setup(r => r.SaveAsync(It.IsAny<CartItem>()))
            .Callback<CartItem>(item => savedItem = item)
            .ReturnsAsync((CartItem item) => item);
        cartRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new Cart(null) { Id = 1 });

        var sut = new CartService(
            cartRepo.Object,
            cartItemRepo.Object,
            Mock.Of<ICustomerCartRepository>(),
            Mock.Of<ICurrencyConversionService>(),
            context);

        await sut.AddItemToCartAsync(cartId: 1, productId: product.Id, quantity: 3, unitPrice: attackerSuppliedPrice);

        Assert.NotNull(savedItem);
        Assert.Equal(realPrice, savedItem!.UnitPrice);
        Assert.NotEqual(attackerSuppliedPrice, savedItem.UnitPrice);
    }

    [Fact]
    public async Task AddItemToCartAsync_ExistingItem_AccumulatesQuantityAndRepricesFromProduct()
    {
        using var context = _dbFactory.CreateContext();
        var product = new Product
        {
            Name = "Test Widget",
            Barcode = "TEST-002",
            SellingPrice = 10.00m,
            StockQuantity = 100,
            SupplierId = 1,
            CurrencyId = 1,
            CategoryId = 1,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var existingItem = new CartItem(cartId: 1, productId: product.Id, quantity: 2, unitPrice: 999m);

        var cartRepo = new Mock<ICartRepository>();
        var cartItemRepo = new Mock<ICartItemRepository>();
        cartItemRepo
            .Setup(r => r.GetByCartAndProductAsync(1, product.Id))
            .ReturnsAsync(existingItem);
        cartItemRepo
            .Setup(r => r.SaveAsync(It.IsAny<CartItem>()))
            .ReturnsAsync((CartItem item) => item);
        cartRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new Cart(null) { Id = 1 });

        var sut = new CartService(
            cartRepo.Object,
            cartItemRepo.Object,
            Mock.Of<ICustomerCartRepository>(),
            Mock.Of<ICurrencyConversionService>(),
            context);

        await sut.AddItemToCartAsync(cartId: 1, productId: product.Id, quantity: 5, unitPrice: 0.01m);

        Assert.Equal(7, existingItem.Quantity); // 2 + 5
        Assert.Equal(10.00m, existingItem.UnitPrice); // repriced from Product, not the caller's 0.01
    }

    public void Dispose() => _dbFactory.Dispose();
}
