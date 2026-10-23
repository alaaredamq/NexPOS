using Core.Entities.Products;
using Infrastructure.Data;
using Infrastructure.Repositories.Products;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Repositories;

/// <summary>
/// The generic repository's GetByIdAsync/GetAllAsync never load navigations, so a category's
/// Products collection was always empty on the Details page and the Index "Products" column
/// showed 0 even for categories that had products.
/// </summary>
public class ProductCategoryRepositoryTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly IServiceProvider _emptyServiceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task GetByIdWithProductsAsync_LoadsOnlyThisCategorysLiveProducts_OrderedByName()
    {
        using var context = _dbFactory.CreateContext();
        var pieces = await AddCategoryAsync(context, "Piece Items");
        var other = await AddCategoryAsync(context, "Other Items");
        AddProduct(context, pieces, "Ballpoint Pen - Blue", "CAT-001");
        AddProduct(context, pieces, "Adhesive Tape", "CAT-002");
        AddProduct(context, pieces, "Discontinued Eraser", "CAT-003", isDeleted: true);
        AddProduct(context, other, "Unrelated Item", "CAT-004");
        await context.SaveChangesAsync();

        // Fresh context so nothing is served from the change tracker's identity map.
        using var readContext = _dbFactory.CreateContext();
        var repo = new ProductCategoryRepository(readContext, _emptyServiceProvider);

        var category = await repo.GetByIdWithProductsAsync(pieces.Id);

        Assert.NotNull(category);
        Assert.Equal(new[] { "Adhesive Tape", "Ballpoint Pen - Blue" }, category!.Products.Select(p => p.Name));
        // The Details view formats prices with product.Currency?.Notation.
        Assert.All(category.Products, p => Assert.NotNull(p.Currency));
    }

    [Fact]
    public async Task GetByIdWithProductsAsync_UnknownId_ReturnsNull()
    {
        using var context = _dbFactory.CreateContext();
        var repo = new ProductCategoryRepository(context, _emptyServiceProvider);

        Assert.Null(await repo.GetByIdWithProductsAsync(999_999));
    }

    [Fact]
    public async Task GetProductCountsAsync_CountsLiveProductsPerCategory_OmitsEmptyCategories()
    {
        using var context = _dbFactory.CreateContext();
        var pieces = await AddCategoryAsync(context, "Piece Items");
        var empty = await AddCategoryAsync(context, "Empty Category");
        AddProduct(context, pieces, "Ballpoint Pen - Blue", "CNT-001");
        AddProduct(context, pieces, "Adhesive Tape", "CNT-002");
        AddProduct(context, pieces, "Discontinued Eraser", "CNT-003", isDeleted: true);
        await context.SaveChangesAsync();

        var repo = new ProductCategoryRepository(context, _emptyServiceProvider);

        var counts = await repo.GetProductCountsAsync();

        Assert.Equal(2, counts[pieces.Id]);
        Assert.False(counts.ContainsKey(empty.Id));
    }

    private static async Task<ProductCategory> AddCategoryAsync(ErpDbContext context, string name)
    {
        var category = new ProductCategory(name);
        context.Set<ProductCategory>().Add(category);
        await context.SaveChangesAsync();
        return category;
    }

    private static void AddProduct(ErpDbContext context, ProductCategory category, string name, string barcode, bool isDeleted = false)
    {
        context.Products.Add(new Product
        {
            Name = name,
            Barcode = barcode,
            SellingPrice = 1m,
            StockQuantity = 5,
            SupplierId = 1,
            CurrencyId = 1,
            CategoryId = category.Id,
            IsActive = true,
            IsDeleted = isDeleted
        });
    }

    public void Dispose() => _dbFactory.Dispose();
}
