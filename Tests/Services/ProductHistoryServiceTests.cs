using Application.IServices.Base;
using Application.IServices.Products;
using Application.Services.Products;
using Core.Entities.Products;
using Core.ICache;
using Core.IRepositories.Products;
using Infrastructure.Data;
using Infrastructure.Repositories.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Services;

/// <summary>
/// A product's history has to say what really happened to it: the units added or removed (and why), what the stock was
/// before and after, price and detail changes - and nothing at all for a save that changed nothing.
/// </summary>
public class ProductHistoryServiceTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private int _barcode;

    public void Dispose() => _dbFactory.Dispose();

    private ProductService CreateProducts(ErpDbContext context) =>
        new(new ProductRepository(context, _services), Mock.Of<ICurrencyConversionService>(), Mock.Of<ICacheService>(),
            TestProductHistory.Create(context));

    private Product NewProduct(decimal stock = 10m) => new()
    {
        Name = "Test Widget", Barcode = $"HIS-{++_barcode}", SellingPrice = 2.5m, CostPrice = 1m, StockQuantity = stock,
        MinimumStock = 2m, SupplierId = 1, CurrencyId = 1, CategoryId = 1, BrandId = 1, IsActive = true, Unit = "piece"
    };

    private async Task<List<ProductHistory>> HistoryOf(int productId)
    {
        using var check = _dbFactory.CreateContext();
        return (await check.ProductHistories.Where(h => h.ProductId == productId).ToListAsync())
            .OrderBy(h => h.Id).ToList();
    }

    [Fact]
    public async Task CreatingAProduct_RecordsItAsCreated_WithTheOpeningStock()
    {
        using var context = _dbFactory.CreateContext();
        var created = await CreateProducts(context).CreateAsync(NewProduct(stock: 25m));

        var line = Assert.Single(await HistoryOf(created.Id));
        Assert.Equal(ProductHistoryType.Created, line.Type);
        Assert.Equal(25m, line.StockAfter);
        Assert.Equal("alice", line.UserName);
        Assert.Equal(7, line.UserId);
    }

    [Fact]
    public async Task AddingStockByHand_RecordsTheUnitsTheReasonAndBeforeAfter()
    {
        using var context = _dbFactory.CreateContext();
        var products = CreateProducts(context);
        var created = await products.CreateAsync(NewProduct(stock: 10m));

        // The caller edits the tracked product and hands it back - the same instance the service loads.
        created.StockQuantity = 25m;
        await products.UpdateAsync(created.Id, created, new ProductChangeContext(ProductHistoryType.StockAdded, "  Delivery from Acme  "));

        var lines = await HistoryOf(created.Id);
        var added = Assert.Single(lines, l => l.Type == ProductHistoryType.StockAdded);
        Assert.Equal(15m, added.QuantityChange);
        Assert.Equal(10m, added.StockBefore);
        Assert.Equal(25m, added.StockAfter);
        Assert.Equal("Delivery from Acme", added.Reason);
        Assert.Equal(2, lines.Count); // Created + StockAdded, nothing else
    }

    [Fact]
    public async Task RemovingStockByHand_IsANegativeChange()
    {
        using var context = _dbFactory.CreateContext();
        var products = CreateProducts(context);
        var created = await products.CreateAsync(NewProduct(stock: 10m));

        created.StockQuantity = 7m;
        await products.UpdateAsync(created.Id, created, new ProductChangeContext(ProductHistoryType.StockRemoved, "Damaged"));

        var removed = Assert.Single(await HistoryOf(created.Id), l => l.Type == ProductHistoryType.StockRemoved);
        Assert.Equal(-3m, removed.QuantityChange);
        Assert.Equal(10m, removed.StockBefore);
        Assert.Equal(7m, removed.StockAfter);
    }

    [Fact]
    public async Task TypingANewStockFigureOnTheForm_IsRecordedAsSet_NotAsAdded()
    {
        using var context = _dbFactory.CreateContext();
        var products = CreateProducts(context);
        var created = await products.CreateAsync(NewProduct(stock: 10m));

        created.StockQuantity = 4m;
        await products.UpdateAsync(created.Id, created);

        var set = Assert.Single(await HistoryOf(created.Id), l => l.Type == ProductHistoryType.StockSet);
        Assert.Equal(-6m, set.QuantityChange);
        Assert.Equal(4m, set.StockAfter);
    }

    [Fact]
    public async Task PriceMinimumAndDetailChanges_EachGetTheirOwnLine()
    {
        using var context = _dbFactory.CreateContext();
        var products = CreateProducts(context);
        var created = await products.CreateAsync(NewProduct());

        created.SellingPrice = 3m;
        created.CostPrice = 1.25m;
        created.MinimumStock = 5m;
        created.Name = "Renamed Widget";
        created.IsActive = false;
        await products.UpdateAsync(created.Id, created);

        var lines = await HistoryOf(created.Id);
        var selling = Assert.Single(lines, l => l.Type == ProductHistoryType.SellingPriceChanged);
        Assert.Equal("2.5", selling.OldValue);
        Assert.Equal("3", selling.NewValue);
        var cost = Assert.Single(lines, l => l.Type == ProductHistoryType.CostPriceChanged);
        Assert.Equal("1", cost.OldValue);
        Assert.Equal("1.25", cost.NewValue);
        var minimum = Assert.Single(lines, l => l.Type == ProductHistoryType.MinimumStockChanged);
        Assert.Equal("2", minimum.OldValue);
        Assert.Equal("5", minimum.NewValue);
        var updated = Assert.Single(lines, l => l.Type == ProductHistoryType.Updated);
        Assert.Equal("name,deactivated", updated.Details);
        Assert.DoesNotContain(lines, l => l.Type is ProductHistoryType.StockSet or ProductHistoryType.StockAdded);
    }

    [Fact]
    public async Task ASaveThatChangesNothing_AddsNoHistory()
    {
        using var context = _dbFactory.CreateContext();
        var products = CreateProducts(context);
        var created = await products.CreateAsync(NewProduct());

        await products.UpdateAsync(created.Id, created);

        Assert.Single(await HistoryOf(created.Id)); // only "Created"
    }

    [Fact]
    public async Task DeletingAProduct_IsRecorded_AndTheHistoryIsKept()
    {
        using var context = _dbFactory.CreateContext();
        var products = CreateProducts(context);
        var created = await products.CreateAsync(NewProduct());

        await products.DeleteAsync(created.Id);

        var lines = await HistoryOf(created.Id);
        Assert.Equal([ProductHistoryType.Created, ProductHistoryType.Deleted], lines.Select(l => l.Type));
    }

    [Fact]
    public async Task AMovementFromASale_ReadsTheStockAfterFromTheDatabase_AndSumsIntoTheTotals()
    {
        using var context = _dbFactory.CreateContext();
        var created = await CreateProducts(context).CreateAsync(NewProduct(stock: 10m));
        var history = TestProductHistory.Create(context);
        var repository = new ProductRepository(context, _services);

        Assert.True(await repository.TryDeductStockAsync(created.Id, 4m));
        await history.RecordMovementAsync(created.Id, ProductHistoryType.Sold, -4m, orderId: 12);
        await repository.RestoreStockAsync(created.Id, 1m);
        await history.RecordMovementAsync(created.Id, ProductHistoryType.SaleReturned, 1m, orderId: 12, reason: "Wrong item");

        var page = await history.GetPageAsync(created.Id, [ProductHistoryType.Sold, ProductHistoryType.SaleReturned], page: 1, pageSize: 10);

        Assert.Equal(2, page.Total);
        var newest = page.Items[0];
        Assert.Equal(ProductHistoryType.SaleReturned, newest.Type); // newest first
        Assert.Equal(7m, newest.StockAfter);
        var sold = page.Items[1];
        Assert.Equal(-4m, sold.QuantityChange);
        Assert.Equal(10m, sold.StockBefore);
        Assert.Equal(6m, sold.StockAfter);
        Assert.Equal(12, sold.OrderId);
        Assert.Equal(4m, page.Totals.Sold);
        Assert.Equal(1m, page.Totals.Returned);
    }

    [Fact]
    public async Task AFailingHistoryWrite_DoesNotFailTheOperation()
    {
        using var context = _dbFactory.CreateContext();
        var created = await CreateProducts(context).CreateAsync(NewProduct());

        // A line pointing at a product that does not exist violates the foreign key.
        await TestProductHistory.Create(context).RecordMovementAsync(99999, ProductHistoryType.Sold, -1m);

        // ...and the context is still usable for the next save.
        created.Name = "Still works";
        await context.SaveChangesAsync();
    }
}
