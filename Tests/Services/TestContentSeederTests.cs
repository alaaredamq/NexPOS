using Core.Entities.Products;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Services;

/// <summary>
/// The bilingual test catalogue must cover piece, volume and mass units in both English and
/// Arabic, round-trip Arabic text intact, and be safe to run repeatedly.
/// </summary>
public class TestContentSeederTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();

    private static bool HasArabic(string s) => s.Any(c => c is >= '؀' and <= 'ۿ');

    [Fact]
    public async Task SeedAsync_CoversPieceVolumeAndMass_InBothLanguages()
    {
        using var db = _dbFactory.CreateContext();

        var added = await TestContentSeeder.SeedAsync(db);

        var products = await db.Products.Where(p => p.Barcode.StartsWith("6002")).ToListAsync();
        Assert.Equal(added, products.Count);

        foreach (var arabic in new[] { false, true })
        {
            var group = products.Where(p => HasArabic(p.Name) == arabic).ToList();
            Assert.Contains(group, p => p.Unit == ProductUnit.Piece);
            Assert.Contains(group, p => ProductUnit.IsVolumeUnit(p.Unit));
            Assert.Contains(group, p => ProductUnit.IsWeightUnit(p.Unit));
        }

        Assert.All(products, p => Assert.True(ProductUnit.IsValidUnit(p.Unit)));
        Assert.Equal(products.Count, products.Select(p => p.Barcode).Distinct().Count());
        Assert.All(products, p => Assert.Equal(13, p.Barcode.Length));
    }

    [Fact]
    public async Task SeedAsync_RoundTripsArabicText()
    {
        using (var db = _dbFactory.CreateContext())
            await TestContentSeeder.SeedAsync(db);

        using var read = _dbFactory.CreateContext();
        Assert.True(await read.Products.AnyAsync(p => p.Name == "زيت زيتون (باللتر)"));
        Assert.True(await read.ProductCategories.AnyAsync(c => c.Name == "منتجات بالقطعة"));
        Assert.True(await read.Suppliers.AnyAsync(s => s.Name == "شركة النور للتوريد"));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var db = _dbFactory.CreateContext();

        var first = await TestContentSeeder.SeedAsync(db);
        var productsAfterFirst = await db.Products.CountAsync();
        var categoriesAfterFirst = await db.ProductCategories.CountAsync();
        var suppliersAfterFirst = await db.Suppliers.CountAsync();

        var second = await TestContentSeeder.SeedAsync(db);

        Assert.True(first > 0);
        Assert.Equal(0, second);
        Assert.Equal(productsAfterFirst, await db.Products.CountAsync());
        Assert.Equal(categoriesAfterFirst, await db.ProductCategories.CountAsync());
        Assert.Equal(suppliersAfterFirst, await db.Suppliers.CountAsync());
    }

    public void Dispose() => _dbFactory.Dispose();
}
