using System.Text;
using Application.IServices.Base;
using Application.IServices.Products;
using Application.Services.Products;
using Core.Entities.Products;
using Core.Entities.Sales;
using Core.ICache;
using Core.IRepositories.Products;
using Moq;
using Xunit;

namespace Tests.Services;

/// <summary>
/// Covers the product CSV import/export: matching by barcode, blank cells leaving values alone,
/// the check-only preview, per-row errors and the spreadsheet quirks (Excel's sep= line, ';'
/// delimiters, decimal commas, mangled barcodes).
/// </summary>
public class ProductImportExportServiceTests
{
    private const string Header = "Barcode,Name,Description,Category,Supplier,Unit,PurchasePrice,SellingPrice,Stock,MinimumStock,Active";

    private readonly ProductCategory _drinks = new("Drinks") { Id = 1 };
    private readonly Supplier _noSupplier = new("No Supplier") { Id = 1 };
    private readonly Supplier _acme = new("Acme") { Id = 2 };
    private readonly List<Product> _existing = new();
    private readonly List<Product> _saved = new();
    private readonly List<ProductCategory> _createdCategories = new();
    private readonly Mock<IProductRepository> _repo = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly ProductImportExportService _sut;

    public ProductImportExportServiceTests()
    {
        _repo.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>?>()))
            .ReturnsAsync(() => _existing.ToList());
        _repo.Setup(r => r.SaveRangeAsync(It.IsAny<IEnumerable<Product>>()))
            .Callback<IEnumerable<Product>>(added => _saved.AddRange(added))
            .Returns(Task.CompletedTask);

        var categories = new Mock<IProductCategoryService>();
        categories.Setup(c => c.GetAllAsync()).ReturnsAsync(new List<ProductCategory> { _drinks });
        categories.Setup(c => c.CreateAsync(It.IsAny<ProductCategory>()))
            .ReturnsAsync((ProductCategory c) =>
            {
                c.Id = 100 + _createdCategories.Count;
                _createdCategories.Add(c);
                return c;
            });

        var suppliers = new Mock<ISupplierService>();
        suppliers.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<Supplier> { _noSupplier, _acme });

        var currencies = new Mock<ICurrencyService>();
        currencies.Setup(c => c.GetDefaultAsync())
            .ReturnsAsync(new Currency("Dollar", 1f, true, true, "USD", "$") { Id = 7 });

        _sut = new ProductImportExportService(_repo.Object, categories.Object, suppliers.Object, currencies.Object, _cache.Object);
    }

    private Product ExistingCola() => new()
    {
        Id = 10, Name = "Cola", Barcode = "6001", Description = "Fizzy, cold", CostPrice = 1.5m, SellingPrice = 2.25m,
        StockQuantity = 10, MinimumStock = 2, Unit = "piece", IsActive = true,
        CategoryId = 1, Category = _drinks, SupplierId = 2, Supplier = _acme, CurrencyId = 7
    };

    private static MemoryStream Csv(params string[] lines) =>
        new(Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

    [Fact]
    public async Task ExportThenImport_ChangesNothing()
    {
        _existing.Add(ExistingCola());

        var exported = await _sut.ExportAsync();
        var result = await _sut.ImportAsync(new MemoryStream(exported), apply: true);

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(0, result.Created + result.Updated + result.Failed);
        _repo.Verify(r => r.SaveRangeAsync(It.IsAny<IEnumerable<Product>>()), Times.Never);
    }

    [Fact]
    public async Task Export_StartsWithBomAndEscapesFormulas()
    {
        var cola = ExistingCola();
        cola.Name = "=SUM(A1)";
        _existing.Add(cola);

        var bytes = await _sut.ExportAsync();
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.StartsWith("﻿sep=,", text);
        Assert.Contains("'=SUM(A1)", text);
        Assert.Contains("\"Fizzy, cold\"", text);
    }

    [Fact]
    public async Task Import_NewProduct_CreatesItWithDefaultsAndNewCategory()
    {
        var result = await _sut.ImportAsync(Csv(Header,
            "7001,Juice,,Fresh Juice,,,1.2,2.5,40,5,Yes"), apply: true);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.NewCategories);
        var product = Assert.Single(_saved);
        Assert.Equal("7001", product.Barcode);
        Assert.Equal("Juice", product.Name);
        Assert.Equal(2.5m, product.SellingPrice);
        Assert.Equal(40m, product.StockQuantity);
        Assert.Equal(_noSupplier.Id, product.SupplierId); // blank supplier falls back to "No Supplier"
        Assert.Equal(7, product.CurrencyId);              // default currency
        Assert.Equal("piece", product.Unit);
        Assert.Equal(_createdCategories.Single().Id, product.CategoryId);
        _cache.Verify(c => c.ClearPOSCachesAsync(), Times.Once);
    }

    [Fact]
    public async Task Import_ExistingBarcode_UpdatesOnlyTheFilledCells()
    {
        var cola = ExistingCola();
        _existing.Add(cola);

        var result = await _sut.ImportAsync(Csv("Barcode,SellingPrice,Stock", "6001,3.00,"), apply: true);

        Assert.Equal(1, result.Updated);
        Assert.Equal(3.00m, cola.SellingPrice);
        Assert.Equal(10m, cola.StockQuantity);          // blank cell: untouched
        Assert.Equal("Cola", cola.Name);                // column absent: untouched
        Assert.NotNull(cola.UpdatedAt);
        Assert.Empty(_saved);
        _repo.Verify(r => r.SaveRangeAsync(It.IsAny<IEnumerable<Product>>()), Times.Once);
    }

    [Fact]
    public async Task Import_CheckOnly_ReportsButSavesNothing()
    {
        var cola = ExistingCola();
        _existing.Add(cola);

        var result = await _sut.ImportAsync(Csv(Header,
            "6001,Cola,,Drinks,Acme,piece,1.5,9.99,10,2,Yes",
            "7001,Juice,,Brand New,,,1,2,,,"), apply: false);

        Assert.False(result.Applied);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.NewCategories);
        Assert.Equal(2.25m, cola.SellingPrice);
        Assert.Empty(_createdCategories);
        _repo.Verify(r => r.SaveRangeAsync(It.IsAny<IEnumerable<Product>>()), Times.Never);
        _cache.Verify(c => c.ClearPOSCachesAsync(), Times.Never);
    }

    [Fact]
    public async Task Import_BadRows_AreSkippedAndReportedWhileGoodRowsStillImport()
    {
        var result = await _sut.ImportAsync(Csv(Header,
            "7001,Juice,,Drinks,,,1,2,0,0,Yes",
            "7001,Again,,Drinks,,,1,2,0,0,Yes",       // repeated barcode
            "7002,Water,,Drinks,Nobody,,1,2,0,0,Yes", // unknown supplier
            "7003,Milk,,Drinks,,,1,abc,0,0,Yes",      // bad price
            "7004,Tea,,Drinks,,,1,0,0,0,Yes",         // zero price
            ",Nameless,,Drinks,,,1,2,0,0,Yes",        // no barcode
            "6.00123E+12,Mangled,,Drinks,,,1,2,0,0,Yes"), apply: true);

        Assert.Equal(6, result.Failed);
        Assert.Equal(1, result.Created);
        Assert.Equal("7001", Assert.Single(_saved).Barcode);
        Assert.Contains(result.Issues, i => i.Row == 3 && i.Message.Contains("repeated"));
        Assert.Contains(result.Issues, i => i.Row == 4 && i.Message.Contains("Nobody"));
        Assert.Contains(result.Issues, i => i.Row == 5 && i.Message.Contains("SellingPrice"));
        Assert.Contains(result.Issues, i => i.Row == 8 && i.Message.Contains("Excel"));
    }

    [Fact]
    public async Task Import_FailedRowDoesNotCreateItsCategory()
    {
        var result = await _sut.ImportAsync(Csv(Header, "7001,Juice,,Ghost Category,,,1,abc,0,0,Yes"), apply: true);

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.NewCategories);
        Assert.Empty(_createdCategories);
    }

    [Fact]
    public async Task Import_ReadsSemicolonFilesDecimalCommasAndQuotedCells()
    {
        var result = await _sut.ImportAsync(Csv(
            "barcode;product name;price;category;active",
            "7001;\"Chips; salted\";\"1.234,50\";Drinks;نعم",
            "7002;\"Two\r\nlines\";12,5;Drinks;no"), apply: true);

        Assert.Equal(2, result.Created);
        var chips = _saved.Single(p => p.Barcode == "7001");
        Assert.Equal("Chips; salted", chips.Name);
        Assert.Equal(1234.50m, chips.SellingPrice);
        Assert.True(chips.IsActive);
        var lines = _saved.Single(p => p.Barcode == "7002");
        Assert.Equal("Two\r\nlines", lines.Name);
        Assert.Equal(12.5m, lines.SellingPrice);
        Assert.False(lines.IsActive);
    }

    [Fact]
    public async Task Import_HonoursExcelSepLine()
    {
        var result = await _sut.ImportAsync(Csv("sep=;", "Barcode;Name;Category;SellingPrice", "7001;Juice;Drinks;2"), apply: true);

        Assert.Equal(1, result.Created);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Name,Price\r\nJuice,2")]            // no Barcode column
    [InlineData("Barcode,Name\r\n7001,\"unclosed")]  // quote never closes
    public async Task Import_UnusableFile_IsRejectedWholesale(string content)
    {
        var result = await _sut.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(content)), apply: true);

        Assert.False(result.Success);
        Assert.NotNull(result.FileError);
        _repo.Verify(r => r.SaveRangeAsync(It.IsAny<IEnumerable<Product>>()), Times.Never);
    }

    [Fact]
    public async Task Import_NotUtf8_IsRejectedWithAHint()
    {
        var latin1 = Encoding.Latin1.GetBytes("Barcode,Name\r\n7001,Café");

        var result = await _sut.ImportAsync(new MemoryStream(latin1), apply: true);

        Assert.Contains("UTF-8", result.FileError);
    }

    [Fact]
    public void Template_HasTheHeaderAndAnExampleRow()
    {
        var text = Encoding.UTF8.GetString(_sut.BuildTemplate());

        Assert.Contains(Header, text);
        Assert.Contains("Sample product", text);
    }
}
