using Core.Entities.Products;
using Infrastructure.Repositories.Products;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Repositories;

/// <summary>
/// Covers S5: stock deduction must be atomic (a single conditional UPDATE), so a concurrent or
/// insufficient-stock deduction can never take a product negative.
/// </summary>
public class ProductRepositoryTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly IServiceProvider _emptyServiceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task TryDeductStockAsync_SufficientStock_DeductsAndReturnsTrue()
    {
        using var context = _dbFactory.CreateContext();
        var product = new Product
        {
            Name = "Stock Test Widget",
            Barcode = "STOCK-001",
            SellingPrice = 5m,
            StockQuantity = 10,
            SupplierId = 1,
            CurrencyId = 1,
            CategoryId = 1,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var repo = new ProductRepository(context, _emptyServiceProvider);

        var result = await repo.TryDeductStockAsync(product.Id, 4);

        Assert.True(result);
        // ExecuteUpdateAsync bypasses the change tracker, so re-reading through the SAME context
        // via FindAsync would return the stale tracked instance from its identity map instead of
        // hitting the DB - use a fresh context (same underlying connection/data) to verify.
        using var verifyContext = _dbFactory.CreateContext();
        var reloaded = await verifyContext.Products.FindAsync(product.Id);
        Assert.Equal(6, reloaded!.StockQuantity);
    }

    [Fact]
    public async Task TryDeductStockAsync_InsufficientStock_ReturnsFalseAndLeavesStockUnchanged()
    {
        using var context = _dbFactory.CreateContext();
        var product = new Product
        {
            Name = "Stock Test Widget",
            Barcode = "STOCK-002",
            SellingPrice = 5m,
            StockQuantity = 3,
            SupplierId = 1,
            CurrencyId = 1,
            CategoryId = 1,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var repo = new ProductRepository(context, _emptyServiceProvider);

        var result = await repo.TryDeductStockAsync(product.Id, 10);

        Assert.False(result);
        using var verifyContext = _dbFactory.CreateContext();
        var reloaded = await verifyContext.Products.FindAsync(product.Id);
        Assert.Equal(3, reloaded!.StockQuantity); // untouched
    }

    [Fact]
    public async Task TryDeductStockAsync_ExactStock_SucceedsAndLeavesZero()
    {
        using var context = _dbFactory.CreateContext();
        var product = new Product
        {
            Name = "Stock Test Widget",
            Barcode = "STOCK-003",
            SellingPrice = 5m,
            StockQuantity = 5,
            SupplierId = 1,
            CurrencyId = 1,
            CategoryId = 1,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var repo = new ProductRepository(context, _emptyServiceProvider);

        var result = await repo.TryDeductStockAsync(product.Id, 5);

        Assert.True(result);
        using var verifyContext = _dbFactory.CreateContext();
        var reloaded = await verifyContext.Products.FindAsync(product.Id);
        Assert.Equal(0, reloaded!.StockQuantity);
    }

    public void Dispose() => _dbFactory.Dispose();
}
