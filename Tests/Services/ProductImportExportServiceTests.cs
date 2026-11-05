using System.Text;
using Application.DTOs.Products;
using Application.IServices.Base;
using Application.IServices.Products;
using Application.Services.Products;
using ClosedXML.Excel;
using Core.Entities.Products;
using Core.Entities.Sales;
using Core.ICache;
using Core.IRepositories.Products;
using Moq;
using Xunit;

namespace Tests.Services;

/// <summary>
/// Covers the product import/export: the Excel workbook (dropdowns, a Custom Prices sheet with several
/// rows per product), matching by barcode, blank cells leaving values alone, the check-only preview,
/// per-row errors, and the CSV quirks still accepted (Excel's sep= line, ';' delimiters, decimal commas,
/// mangled barcodes).
/// </summary>
public class ProductImportExportServiceTests
{
    private const string Header = "Barcode,Name,Description,Category,Supplier,Unit,PurchasePrice,SellingPrice,Stock,MinimumStock,Active";
    private static readonly string[] PriceHeader = ["Barcode", "PriceName", "Price"];

    private readonly ProductCategory _drinks = new("Drinks") { Id = 1 };
    private readonly Supplier _noSupplier = new("No Supplier") { Id = 1 };
    private readonly Supplier _acme = new("Acme") { Id = 2 };
    private readonly List<Product> _existing = new();
    private readonly List<Product> _saved = new();
    private readonly List<ProductCategory> _createdCategories = new();
    private readonly Dictionary<int, List<ProductPrice>> _tiers = new();
    private readonly Dictionary<int, List<ProductPriceInput>> _savedTiers = new();
    private readonly Mock<IProductRepository> _repo = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly ProductImportExportService _sut;

    public ProductImportExportServiceTests()
    {
        _repo.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>?>()))
            .ReturnsAsync(() => _existing.ToList());
        _repo.Setup(r => r.SaveRangeAsync(It.IsAny<IEnumerable<Product>>()))
            .Callback<IEnumerable<Product>>(added =>
            {
                foreach (var product in added)
                {
                    product.Id = 500 + _saved.Count;
                    _saved.Add(product);
                }
            })
            .Returns(Task.CompletedTask);

        var prices = new Mock<IProductPriceService>();
        prices.Setup(p => p.GetAllTiersAsync()).ReturnsAsync(() => _tiers.ToDictionary(t => t.Key, t => t.Value));
        prices.Setup(p => p.ReplaceTiersAsync(It.IsAny<IReadOnlyDictionary<int, List<ProductPriceInput>>>()))
            .Callback<IReadOnlyDictionary<int, List<ProductPriceInput>>>(plan =>
            {
                foreach (var (id, tiers) in plan) _savedTiers[id] = tiers;
            })
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

        _sut = new ProductImportExportService(_repo.Object, prices.Object, categories.Object, suppliers.Object, currencies.Object, _cache.Object, Mock.Of<Application.IServices.Products.IProductHistoryService>());
    }

    private Product ExistingCola() => new()
    {
        Id = 10, Name = "Cola", Barcode = "6001", Description = "Fizzy, cold", CostPrice = 1.5m, SellingPrice = 2.25m,
        StockQuantity = 10, MinimumStock = 2, Unit = "piece", IsActive = true,
        CategoryId = 1, Category = _drinks, SupplierId = 2, Supplier = _acme, CurrencyId = 7
    };

    private static MemoryStream Csv(params string[] lines) =>
        new(Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

    private static List<ProductPrice> Tiers(int productId, params (string Name, decimal Price)[] tiers) =>
        tiers.Select((t, i) => new ProductPrice { Id = productId * 10 + i, ProductId = productId, Name = t.Name, Price = t.Price, SortOrder = i }).ToList();

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    private static MemoryStream Workbook(string[][] products, string[][]? prices = null)
    {
        using var book = new XLWorkbook();
        Fill(book.AddWorksheet("Products"), products);
        if (prices != null) Fill(book.AddWorksheet("Custom Prices"), prices);
        return Save(book);
    }

    private static MemoryStream Save(XLWorkbook book)
    {
        var stream = new MemoryStream();
        book.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static void Fill(IXLWorksheet sheet, string[][] rows)
    {
        for (var r = 0; r < rows.Length; r++)
        for (var c = 0; c < rows[r].Length; c++)
            if (rows[r][c].Length > 0) sheet.Cell(r + 1, c + 1).Value = rows[r][c];
    }

    #region Export and template

    [Fact]
    public async Task ExportThenImport_ChangesNothing()
    {
        var cola = ExistingCola();
        _existing.Add(cola);
        _tiers[cola.Id] = Tiers(cola.Id, ("Wholesale", 2.00m), ("Staff", 1.80m));

        var exported = await _sut.ExportAsync();
        var result = await _sut.ImportAsync(new MemoryStream(exported), apply: true);

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(2, result.TotalPriceRows);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(0, result.Created + result.Updated + result.Failed + result.CustomPrices);
        _repo.Verify(r => r.SaveRangeAsync(It.IsAny<IEnumerable<Product>>()), Times.Never);
        Assert.Empty(_savedTiers);
    }

    [Fact]
    public async Task Export_WritesProductsAndOneRowPerCustomPrice()
    {
        var cola = ExistingCola();
        _existing.Add(cola);
        _tiers[cola.Id] = Tiers(cola.Id, ("Wholesale", 2.00m), ("Staff", 1.80m));

        using var book = Open(await _sut.ExportAsync());

        var products = book.Worksheet("Products");
        Assert.Equal("6001", products.Cell(2, 1).GetString());
        Assert.Equal("Drinks", products.Cell(2, 4).GetString());
        Assert.Equal("Acme", products.Cell(2, 5).GetString());
        Assert.Equal(2.25, products.Cell(2, 8).GetDouble());

        var prices = book.Worksheet("Custom Prices");
        Assert.Equal("6001", prices.Cell(2, 1).GetString());
        Assert.Equal("Wholesale", prices.Cell(2, 2).GetString());
        Assert.Equal("6001", prices.Cell(3, 1).GetString());
        Assert.Equal("Staff", prices.Cell(3, 2).GetString());
        Assert.Equal(1.80, prices.Cell(3, 3).GetDouble());
    }

    [Fact]
    public async Task Export_KeepsFormulaLookingTextAsText()
    {
        var cola = ExistingCola();
        cola.Name = "=SUM(A1)";
        _existing.Add(cola);

        var bytes = await _sut.ExportAsync();
        using var book = Open(bytes);
        var cell = book.Worksheet("Products").Cell(2, 2);

        Assert.False(cell.HasFormula);
        Assert.Equal("=SUM(A1)", cell.GetString());

        var again = await _sut.ImportAsync(new MemoryStream(bytes), apply: true);
        Assert.Equal(1, again.Unchanged);
    }

    [Fact]
    public async Task Template_HasDropdownsFilledFromTheCurrentCategoriesAndSuppliers()
    {
        using var book = Open(await _sut.BuildTemplateAsync());
        var products = book.Worksheet("Products");

        Assert.Equal("Barcode", products.Cell(1, 1).GetString());
        Assert.Equal("Sample product", products.Cell(2, 2).GetString());
        Assert.Equal("Drinks", products.Cell(2, 4).GetString());

        var lists = book.Worksheet("Lists");
        Assert.Equal(XLWorksheetVisibility.Hidden, lists.Visibility);
        Assert.Equal("Drinks", lists.Cell(2, 1).GetString());
        Assert.Equal("Acme", lists.Cell(2, 2).GetString());
        Assert.Equal("No Supplier", lists.Cell(3, 2).GetString());

        // Category (D) warns on unknown values since import creates them; Supplier (E) and Unit (F) refuse them.
        var category = products.Cell("D2").GetDataValidation();
        Assert.Equal(XLAllowedValues.List, category.AllowedValues);
        Assert.Equal(XLErrorStyle.Warning, category.ErrorStyle);
        var supplier = products.Cell("E2").GetDataValidation();
        Assert.Equal(XLAllowedValues.List, supplier.AllowedValues);
        Assert.Equal(XLErrorStyle.Stop, supplier.ErrorStyle);
        Assert.Equal(XLAllowedValues.List, products.Cell("F2").GetDataValidation().AllowedValues);
        Assert.Equal(XLAllowedValues.Decimal, products.Cell("H2").GetDataValidation().AllowedValues);

        // The sample shows two prices on the same product.
        var prices = book.Worksheet("Custom Prices");
        Assert.Equal(prices.Cell(2, 1).GetString(), prices.Cell(3, 1).GetString());
        Assert.NotEqual(prices.Cell(2, 2).GetString(), prices.Cell(3, 2).GetString());
    }

    [Fact]
    public async Task TemplateImportedAsIs_CreatesTheSampleProductAndItsTwoPrices()
    {
        var result = await _sut.ImportAsync(new MemoryStream(await _sut.BuildTemplateAsync()), apply: true);

        Assert.True(result.Success, result.FileError);
        Assert.Empty(result.Issues);
        Assert.Equal(1, result.Created);
        Assert.Equal(2, result.CustomPrices);
        var product = Assert.Single(_saved);
        Assert.Equal(new[] { "Wholesale", "Staff" }, _savedTiers[product.Id].Select(t => t.Name));
    }

    #endregion

    #region CSV

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

    #endregion

    #region Workbook and custom prices

    [Fact]
    public async Task Import_Workbook_NewProductWithSeveralPrices()
    {
        var result = await _sut.ImportAsync(Workbook(
            [
                ["Barcode", "Name", "Category", "PurchasePrice", "SellingPrice"],
                ["7001", "Juice", "Drinks", "1", "2.5"],
            ],
            [
                PriceHeader,
                ["7001", "Wholesale", "2"],
                ["7001", "Staff", "1.75"],
                ["7001", "VIP", "2.25"],
            ]), apply: true);

        Assert.Equal(1, result.Created);
        Assert.Equal(3, result.CustomPrices);
        var product = Assert.Single(_saved);
        Assert.Equal(new[] { ("Wholesale", 2m), ("Staff", 1.75m), ("VIP", 2.25m) },
            _savedTiers[product.Id].Select(t => (t.Name, t.Price)));
    }

    [Fact]
    public async Task Import_Workbook_ReplacesTheSetOnlyForProductsListed()
    {
        var cola = ExistingCola();
        var pepsi = new Product
        {
            Id = 11, Name = "Pepsi", Barcode = "6002", SellingPrice = 2m, CostPrice = 1m, CategoryId = 1, Category = _drinks,
            SupplierId = 2, Supplier = _acme, CurrencyId = 7, Unit = "piece", IsActive = true
        };
        _existing.AddRange([cola, pepsi]);
        _tiers[cola.Id] = Tiers(cola.Id, ("Wholesale", 2.00m), ("Staff", 1.80m));
        _tiers[pepsi.Id] = Tiers(pepsi.Id, ("Wholesale", 1.50m));

        var result = await _sut.ImportAsync(Workbook(
            [["Barcode"], ["6001"]],           // Pepsi isn't listed anywhere and isn't touched
            [PriceHeader, ["6001", "Wholesale", "1.9"]]), apply: true);

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.CustomPrices);
        var saved = Assert.Single(_savedTiers);
        Assert.Equal(cola.Id, saved.Key);
        var only = Assert.Single(saved.Value); // Staff is gone
        Assert.Equal(("Wholesale", 1.9m), (only.Name, only.Price));
    }

    [Fact]
    public async Task Import_Workbook_PricesForAnExistingProductNeedNotBeOnTheProductsSheet()
    {
        _existing.Add(ExistingCola());

        var result = await _sut.ImportAsync(Workbook(
            [["Barcode", "Name"]],
            [PriceHeader, ["6001", "Staff", "2"]]), apply: true);

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.CustomPrices);
        Assert.Equal(0, result.TotalRows);
        Assert.Equal("Staff", _savedTiers[10].Single().Name);
    }

    [Fact]
    public async Task Import_Workbook_OneBadPriceHoldsBackTheWholeSetForThatProduct()
    {
        var cola = ExistingCola();            // purchase price 1.5
        _existing.Add(cola);
        _tiers[cola.Id] = Tiers(cola.Id, ("Wholesale", 2.00m));

        var result = await _sut.ImportAsync(Workbook(
            [["Barcode"], ["6001"]],
            [
                PriceHeader,
                ["6001", "Wholesale", "2.5"],
                ["6001", "Staff", "1"],            // below the purchase price
                ["9999", "Wholesale", "2"],        // no such product
                ["6001", "wholesale", "2.6"],      // same name twice
                ["6001", "", "2"],
            ]), apply: true);

        Assert.Equal(4, result.Failed);
        Assert.Equal(0, result.CustomPrices);
        Assert.Empty(_savedTiers);             // Wholesale 2.00 stays; the good 2.5 row alone isn't applied
        Assert.Contains(result.Issues, i => i.Sheet == "Custom Prices" && i.Row == 3 && i.Message.Contains("purchase price") && i.Message.Contains("unchanged"));
        Assert.Contains(result.Issues, i => i.Sheet == "Custom Prices" && i.Row == 4 && i.Message.Contains("9999"));
        Assert.Contains(result.Issues, i => i.Sheet == "Custom Prices" && i.Row == 5 && i.Message.Contains("twice"));
        Assert.Contains(result.Issues, i => i.Sheet == "Custom Prices" && i.Row == 6 && i.Message.Contains("PriceName"));
    }

    [Fact]
    public async Task Import_Workbook_PricesOfASkippedProductAreSkippedToo()
    {
        var result = await _sut.ImportAsync(Workbook(
            [["Barcode", "Name", "Category", "SellingPrice"], ["7001", "Juice", "Drinks", "abc"]],
            [PriceHeader, ["7001", "Wholesale", "2"]]), apply: true);

        Assert.Equal(2, result.Failed);
        Assert.Empty(_saved);
        Assert.Empty(_savedTiers);
        Assert.Contains(result.Issues, i => i.Sheet == "Custom Prices" && i.Message.Contains("has errors"));
    }

    [Fact]
    public async Task Import_Workbook_CheckOnlyCountsButSavesNothing()
    {
        _existing.Add(ExistingCola());

        var result = await _sut.ImportAsync(Workbook(
            [["Barcode"], ["6001"]],
            [PriceHeader, ["6001", "Staff", "2"]]), apply: false);

        Assert.False(result.Applied);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.CustomPrices);
        Assert.Empty(_savedTiers);
        _cache.Verify(c => c.ClearPOSCachesAsync(), Times.Never);
    }

    [Fact]
    public async Task Import_Workbook_ReadsNumericBarcodesWithoutExponent()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Products");
        sheet.Cell(1, 1).Value = "Barcode";
        sheet.Cell(1, 2).Value = "Name";
        sheet.Cell(1, 3).Value = "Category";
        sheet.Cell(1, 4).Value = "SellingPrice";
        sheet.Cell(2, 1).Value = 6001234567890d;   // typed as a number, not text
        sheet.Cell(2, 2).Value = "Juice";
        sheet.Cell(2, 3).Value = "Drinks";
        sheet.Cell(2, 4).Value = 2.5d;

        var result = await _sut.ImportAsync(Save(book), apply: true);

        Assert.Equal(1, result.Created);
        Assert.Equal("6001234567890", Assert.Single(_saved).Barcode);
        Assert.Equal(2.5m, _saved[0].SellingPrice);
    }

    [Fact]
    public async Task Import_Workbook_ReportsTheSheetRowNumberEvenWithBlankRowsBetween()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Products");
        sheet.Cell(1, 1).Value = "Barcode";
        sheet.Cell(1, 2).Value = "Name";
        sheet.Cell(6, 1).Value = "7001";           // rows 2-5 are empty

        var result = await _sut.ImportAsync(Save(book), apply: true);

        Assert.Equal(1, result.Failed);
        Assert.Equal(6, Assert.Single(result.Issues).Row);
    }

    [Fact]
    public async Task Import_Workbook_PricesSheetWithoutItsColumnsIsRejected()
    {
        var result = await _sut.ImportAsync(Workbook(
            [["Barcode"], ["6001"]],
            [["Barcode", "Amount"], ["6001", "2"]]), apply: true);

        Assert.False(result.Success);
        Assert.Contains("Custom Prices", result.FileError);
    }

    [Fact]
    public async Task Import_CorruptWorkbook_IsRejected()
    {
        var result = await _sut.ImportAsync(new MemoryStream(Encoding.ASCII.GetBytes("PK not really a zip")), apply: true);

        Assert.False(result.Success);
        Assert.Contains("Excel", result.FileError);
    }

    #endregion
}
