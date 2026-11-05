using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Application.DTOs.Products;
using Application.IServices.Base;
using Application.IServices.Products;
using ClosedXML.Excel;
using Core.Entities.Products;
using Core.ICache;
using Core.IRepositories.Products;

namespace Application.Services.Products;

public class ProductImportExportService : IProductImportExportService
{
    private const int MaxRows = 20000;
    private const int MaxReportedIssues = 200;
    private const int MaxPriceNameLength = 50;
    private const string DefaultSupplierName = "No Supplier";

    private const string ProductsSheet = "Products";
    private const string PricesSheet = "Custom Prices";
    private const string GuideSheet = "Guide";
    private const string ListsSheet = "Lists";

    /// <summary>The product columns, in the order they're exported. The names are the export headers.</summary>
    private enum Col { Barcode, Name, Description, Category, Supplier, Unit, PurchasePrice, SellingPrice, Stock, MinimumStock, Active }

    /// <summary>The Custom Prices sheet: one row per extra price, so a product can have as many as it needs.</summary>
    private enum PriceCol { Barcode, PriceName, Price }

    /// <summary>The units the product form offers.</summary>
    private static readonly string[] Units = ["piece", "kg", "lb", "liter", "gallon", "box", "pack"];

    /// <summary>Header spellings accepted on import (lower-case letters and digits only).</summary>
    private static readonly Dictionary<string, Col> HeaderAliases = new()
    {
        ["barcode"] = Col.Barcode,
        ["name"] = Col.Name, ["productname"] = Col.Name,
        ["description"] = Col.Description,
        ["category"] = Col.Category, ["categoryname"] = Col.Category,
        ["supplier"] = Col.Supplier, ["suppliername"] = Col.Supplier,
        ["unit"] = Col.Unit,
        ["purchaseprice"] = Col.PurchasePrice, ["costprice"] = Col.PurchasePrice, ["cost"] = Col.PurchasePrice,
        ["sellingprice"] = Col.SellingPrice, ["saleprice"] = Col.SellingPrice, ["price"] = Col.SellingPrice,
        ["stock"] = Col.Stock, ["stockquantity"] = Col.Stock, ["quantity"] = Col.Stock,
        ["minimumstock"] = Col.MinimumStock, ["minstock"] = Col.MinimumStock, ["minstocklevel"] = Col.MinimumStock,
        ["active"] = Col.Active, ["isactive"] = Col.Active, ["status"] = Col.Active,
    };

    private static readonly Dictionary<string, PriceCol> PriceHeaderAliases = new()
    {
        ["barcode"] = PriceCol.Barcode, ["productbarcode"] = PriceCol.Barcode,
        ["pricename"] = PriceCol.PriceName, ["name"] = PriceCol.PriceName, ["label"] = PriceCol.PriceName, ["tier"] = PriceCol.PriceName,
        ["price"] = PriceCol.Price, ["amount"] = PriceCol.Price,
    };

    /// <summary>Sheet names (lower-case letters and digits only) read as the custom prices.</summary>
    private static readonly string[] PriceSheetNames = ["customprices", "extraprices", "pricetiers", "prices"];

    private static readonly string[] TrueWords = ["yes", "y", "true", "1", "active", "نعم", "فعال", "نشط"];
    private static readonly string[] FalseWords = ["no", "n", "false", "0", "inactive", "لا", "غير فعال", "غير نشط"];

    // Excel turns long digit strings into 6.00123E+12 and keeps only that on save.
    private static readonly Regex MangledNumber = new(@"^\d+(\.\d+)?E\+\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IProductRepository _products;
    private readonly IProductPriceService _prices;
    private readonly IProductCategoryService _categories;
    private readonly ISupplierService _suppliers;
    private readonly ICurrencyService _currencies;
    private readonly ICacheService _cache;
    private readonly IProductHistoryService _history;

    public ProductImportExportService(IProductRepository products, IProductPriceService prices, IProductCategoryService categories,
        ISupplierService suppliers, ICurrencyService currencies, ICacheService cache, IProductHistoryService history)
    {
        _products = products;
        _prices = prices;
        _categories = categories;
        _suppliers = suppliers;
        _currencies = currencies;
        _cache = cache;
        _history = history;
    }

    #region Export

    public async Task<byte[]> ExportAsync()
    {
        var products = (await _products.GetAllAsync())
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var tiers = await _prices.GetAllTiersAsync();

        var priceRows = products
            .SelectMany(p => tiers.GetValueOrDefault(p.Id)?.Select(t => (p.Barcode, t.Name, t.Price)) ?? [])
            .ToList();

        return await BuildWorkbookAsync(
            products.Select(p => new[]
            {
                Str(p.Barcode), Str(p.Name), Str(p.Description), Str(p.Category?.Name), Str(p.Supplier?.Name), p.Unit ?? "",
                Num(p.CostPrice), Num(p.SellingPrice), Num(p.StockQuantity), Num(p.MinimumStock), p.IsActive ? "Yes" : "No"
            }).ToList(),
            priceRows.Select(r => new object[] { Str(r.Barcode), r.Name, r.Price }).ToList(),
            tiers.Values.SelectMany(v => v.Select(t => t.Name)));
    }

    public async Task<byte[]> BuildTemplateAsync()
    {
        var categories = await _categories.GetAllAsync();
        var suppliers = await _suppliers.GetAllAsync();
        var category = categories.OrderBy(c => c.Name).FirstOrDefault()?.Name ?? "Beverages";
        var supplier = suppliers.FirstOrDefault(s => s.Name.Equals(DefaultSupplierName, StringComparison.OrdinalIgnoreCase))?.Name
                       ?? suppliers.FirstOrDefault()?.Name ?? DefaultSupplierName;

        const string barcode = "6001234567890";
        return await BuildWorkbookAsync(
            [new object[] { barcode, "Sample product", "Optional description", category, supplier, "piece", 1.5m, 2.25m, 100m, 10m, "Yes" }],
            [new object[] { barcode, "Wholesale", 2.00m }, new object[] { barcode, "Staff", 1.80m }],
            []);
    }

    private static string Str(string? value) => value ?? "";

    private static object Num(decimal? value) => value.HasValue ? value.Value : "";

    /// <summary>
    /// The workbook the template and the export share: Products and Custom Prices sheets whose Category,
    /// Supplier, Unit and Active cells are dropdowns, a Guide sheet, and a hidden Lists sheet feeding the dropdowns.
    /// </summary>
    private async Task<byte[]> BuildWorkbookAsync(List<object[]> productRows, List<object[]> priceRows, IEnumerable<string> usedPriceNames)
    {
        var categories = (await _categories.GetAllAsync()).Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        var suppliers = (await _suppliers.GetAllAsync()).Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        var priceNames = usedPriceNames.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

        using var book = new XLWorkbook();
        var products = book.AddWorksheet(ProductsSheet);
        var prices = book.AddWorksheet(PricesSheet);
        var guide = book.AddWorksheet(GuideSheet);
        var lists = book.AddWorksheet(ListsSheet);

        // Lists feeding the dropdowns (row 1 is the heading).
        var listCategories = WriteList(lists, 1, "Categories", categories);
        var listSuppliers = WriteList(lists, 2, "Suppliers", suppliers);
        var listUnits = WriteList(lists, 3, "Units", Units);
        var listYesNo = WriteList(lists, 4, "Yes/No", ["Yes", "No"]);
        var listPriceNames = WriteList(lists, 5, "Price names", priceNames);
        lists.Visibility = XLWorksheetVisibility.Hidden;

        // Products
        var lastProductRow = Math.Min(MaxRows + 1, Math.Max(2000, productRows.Count + 1000));
        WriteSheet(products, Enum.GetNames<Col>(), productRows, widths: [16, 28, 30, 18, 18, 10, 14, 14, 10, 14, 10]);
        products.Column((int)Col.Barcode + 1).Style.NumberFormat.Format = "@"; // keeps leading zeros, stops 6.0012E+12
        foreach (var col in new[] { Col.PurchasePrice, Col.SellingPrice, Col.Stock, Col.MinimumStock })
            products.Column((int)col + 1).Style.NumberFormat.Format = "0.00##";

        // A new category is created on import, so that dropdown only warns; everything else must match.
        AddList(products, Col.Category, lastProductRow, listCategories, strict: false,
            "Category not in the list", "This category doesn't exist yet. It will be created when you import. Continue?");
        AddList(products, Col.Supplier, lastProductRow, listSuppliers, strict: true,
            "Unknown supplier", "Pick one of the suppliers in the list. Leave it empty for \"" + DefaultSupplierName + "\".");
        AddList(products, Col.Unit, lastProductRow, listUnits, strict: true, "Unknown unit", "Pick a unit from the list.");
        AddList(products, Col.Active, lastProductRow, listYesNo, strict: true, "Yes or No", "Pick Yes or No.");
        AddNumber(products, Col.PurchasePrice, lastProductRow, positive: false);
        AddNumber(products, Col.SellingPrice, lastProductRow, positive: true);
        AddNumber(products, Col.Stock, lastProductRow, positive: false);
        AddNumber(products, Col.MinimumStock, lastProductRow, positive: false);

        // Custom prices: one row per extra price. A product can have as many rows as it needs.
        var lastPriceRow = Math.Min(MaxRows * 3 + 1, Math.Max(2000, priceRows.Count + 1000));
        WriteSheet(prices, Enum.GetNames<PriceCol>(), priceRows, widths: [16, 24, 14]);
        prices.Column((int)PriceCol.Barcode + 1).Style.NumberFormat.Format = "@";
        prices.Column((int)PriceCol.Price + 1).Style.NumberFormat.Format = "0.00##";

        var barcodeValidation = prices.Range(2, (int)PriceCol.Barcode + 1, lastPriceRow, (int)PriceCol.Barcode + 1).CreateDataValidation();
        barcodeValidation.List(products.Range(2, (int)Col.Barcode + 1, lastProductRow, (int)Col.Barcode + 1));
        Configure(barcodeValidation, strict: false, "Barcode not on the Products sheet",
            "This barcode isn't on the Products sheet. It must belong to a product that already exists. Continue?");

        var nameValidation = prices.Range(2, (int)PriceCol.PriceName + 1, lastPriceRow, (int)PriceCol.PriceName + 1).CreateDataValidation();
        nameValidation.List(listPriceNames);
        Configure(nameValidation, strict: false, "New price name", "Pick an existing price name, or type a new one such as Wholesale or Staff.");

        var priceValidation = prices.Range(2, (int)PriceCol.Price + 1, lastPriceRow, (int)PriceCol.Price + 1).CreateDataValidation();
        priceValidation.Decimal.GreaterThan(0);
        Configure(priceValidation, strict: true, "Price", "Enter an amount greater than zero.");

        WriteGuide(guide);

        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    private static void WriteSheet(IXLWorksheet sheet, string[] headers, List<object[]> rows, double[] widths)
    {
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
            sheet.Column(c + 1).Width = widths[c];
        }

        // Numbers and text are set separately so a name like "=SUM(A1)" is stored as text, never as a formula.
        for (var r = 0; r < rows.Count; r++)
        for (var c = 0; c < headers.Length; c++)
        {
            switch (rows[r][c])
            {
                case decimal d: sheet.Cell(r + 2, c + 1).Value = (double)d; break;
                case string { Length: > 0 } s: sheet.Cell(r + 2, c + 1).Value = s; break;
            }
        }

        sheet.SheetView.FreezeRows(1);
    }

    private static IXLRange WriteList(IXLWorksheet lists, int column, string heading, IReadOnlyList<string> values)
    {
        lists.Cell(1, column).Value = heading;
        for (var i = 0; i < values.Count; i++) lists.Cell(i + 2, column).Value = values[i];
        // An empty list still needs a one-cell range for the dropdown to point at.
        return lists.Range(2, column, Math.Max(2, values.Count + 1), column);
    }

    private static void AddList(IXLWorksheet sheet, Col col, int lastRow, IXLRange source, bool strict, string title, string message)
    {
        var validation = sheet.Range(2, (int)col + 1, lastRow, (int)col + 1).CreateDataValidation();
        validation.List(source);
        Configure(validation, strict, title, message);
    }

    private static void AddNumber(IXLWorksheet sheet, Col col, int lastRow, bool positive)
    {
        var validation = sheet.Range(2, (int)col + 1, lastRow, (int)col + 1).CreateDataValidation();
        if (positive) validation.Decimal.GreaterThan(0);
        else validation.Decimal.EqualOrGreaterThan(0);
        Configure(validation, strict: true, "Number", positive ? "Enter an amount greater than zero." : "Enter a number, zero or more.");
    }

    /// <summary>Strict dropdowns refuse other values; the lenient kind warns and lets the user carry on.</summary>
    private static void Configure(IXLDataValidation validation, bool strict, string title, string message)
    {
        validation.IgnoreBlanks = true;
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = strict ? XLErrorStyle.Stop : XLErrorStyle.Warning;
        validation.ErrorTitle = title;
        validation.ErrorMessage = message;
    }

    private static void WriteGuide(IXLWorksheet guide)
    {
        string[] lines =
        [
            "How to use this file",
            "",
            "Products sheet",
            "• One row per product. The Barcode is what identifies a product: a barcode that already exists updates that product, any other barcode adds a new one.",
            "• Category, Supplier, Unit and Active are dropdowns. A category that isn't in the list is created when you import.",
            "• On an existing product, leave a cell empty to keep its current value. A new product needs Name, Category and SellingPrice.",
            "• An empty Supplier on a new product becomes \"" + DefaultSupplierName + "\".",
            "",
            "Custom Prices sheet",
            "• One row per extra price (Wholesale, Staff, ...). A product can have as many rows as you like: repeat its barcode.",
            "• The barcode must belong to a product on the Products sheet or one that already exists.",
            "• If a product has rows on this sheet, they REPLACE its current extra prices. A product with no rows here keeps the ones it has.",
            "• A price can't be lower than the product's purchase price, and a product can't use the same price name twice.",
            "",
            "Before you import",
            "• Delete the sample rows in this template.",
            "• Keep the Barcode columns formatted as Text so Excel doesn't turn them into numbers.",
            "• You can also import a plain CSV with the same product columns (no custom prices).",
        ];
        guide.Column(1).Width = 110;
        for (var i = 0; i < lines.Length; i++)
        {
            var cell = guide.Cell(i + 1, 1);
            cell.Value = lines[i];
            cell.Style.Alignment.WrapText = true;
            if (i == 0) cell.Style.Font.FontSize = 14;
            if (i == 0 || lines[i] is "Products sheet" or "Custom Prices sheet" or "Before you import") cell.Style.Font.Bold = true;
        }
    }

    #endregion

    #region Import

    /// <summary>A non-blank row of the file with the number a spreadsheet shows for it (header = 1).</summary>
    private sealed record SourceRow(int Number, List<string> Cells);

    /// <summary>One product row that passed validation. Blank cells are null and leave an existing product's value alone.</summary>
    private sealed class ParsedRow
    {
        public Product? Existing;
        public string Barcode = "";
        public string? Name, Description, Unit;
        public decimal? Cost, Price, Stock, MinStock;
        public bool? Active;
        public int? CategoryId;
        public string? NewCategory;
        public int? SupplierId;

        /// <summary>The product's full set of extra prices when the Custom Prices sheet lists some for it; null = leave them alone.</summary>
        public List<ProductPriceInput>? Tiers;
    }

    /// <summary>The Custom Prices rows of one barcode, resolved to the product they belong to.</summary>
    private sealed class PriceGroup
    {
        public ParsedRow? Row;
        public Product? Existing;
        public readonly List<ProductPriceInput> Tiers = new();
        public readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<(int Row, string Message)> Errors = new();
    }

    public async Task<ProductImportResult> ImportAsync(Stream file, bool apply)
    {
        var result = new ProductImportResult { Applied = apply };

        List<SourceRow> records;
        List<SourceRow> priceRecords;
        try
        {
            (records, priceRecords) = await ReadFileAsync(file);
        }
        catch (FormatException ex)
        {
            return Rejected(result, ex.Message);
        }

        if (records.Count == 0)
            return Rejected(result, "The file is empty.");

        var columns = MapHeader(records[0].Cells);
        if (!columns.ContainsKey(Col.Barcode))
            return Rejected(result, "The first row must be a header with at least a \"Barcode\" column.");
        if (records.Count - 1 > MaxRows)
            return Rejected(result, $"The file has more than {MaxRows} rows. Split it into smaller files.");

        Dictionary<PriceCol, int>? priceColumns = null;
        if (priceRecords.Count > 0)
        {
            priceColumns = MapPriceHeader(priceRecords[0].Cells);
            if (priceColumns.Count < Enum.GetValues<PriceCol>().Length)
                return Rejected(result, $"The \"{PricesSheet}\" sheet needs a header row with Barcode, PriceName and Price columns.");
            if (priceRecords.Count - 1 > MaxRows * 3)
                return Rejected(result, $"The \"{PricesSheet}\" sheet has too many rows. Split it into smaller files.");
        }

        var existingByBarcode = (await _products.GetAllAsync())
            .Where(p => !string.IsNullOrWhiteSpace(p.Barcode))
            .GroupBy(p => p.Barcode.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var categories = (await _categories.GetAllAsync())
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var suppliers = (await _suppliers.GetAllAsync())
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        suppliers.TryGetValue(DefaultSupplierName, out var defaultSupplier);

        // Pass 1: read and check every row without touching anything.
        var rows = new List<ParsedRow>();
        var failedRows = new HashSet<int>();
        var failedBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var barcodeRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var newCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Report(string? sheet, int rowNo, string message)
        {
            if (result.Issues.Count < MaxReportedIssues)
                result.Issues.Add(new ProductImportIssue(rowNo, message, sheet));
        }

        for (var i = 1; i < records.Count; i++)
        {
            var rowNo = records[i].Number;
            var record = records[i].Cells;
            result.TotalRows++;

            string Cell(Col c) => columns.TryGetValue(c, out var at) && at < record.Count ? Clean(record[at]) : "";

            var errors = new List<string>();
            var row = new ParsedRow { Barcode = Cell(Col.Barcode) };

            if (row.Barcode.Length == 0)
                errors.Add("Barcode is empty.");
            else if (row.Barcode.Length > 50)
                errors.Add("Barcode is longer than 50 characters.");
            else if (MangledNumber.IsMatch(row.Barcode))
                errors.Add($"Barcode \"{row.Barcode}\" was turned into a number by Excel. Format the Barcode column as Text and enter it again.");
            else if (barcodeRows.TryGetValue(row.Barcode, out var firstRow))
                errors.Add($"Barcode is repeated (first on row {firstRow}).");
            else
            {
                barcodeRows[row.Barcode] = rowNo;
                if (existingByBarcode.TryGetValue(row.Barcode, out var matches))
                {
                    if (matches.Count == 1) row.Existing = matches[0];
                    else errors.Add($"Barcode matches {matches.Count} existing products; fix the duplicates first.");
                }
            }
            var isNew = row.Existing == null;

            var name = Cell(Col.Name);
            if (name.Length > 0)
            {
                if (name.Length is < 2 or > 255) errors.Add("Name must be 2-255 characters.");
                else row.Name = name;
            }
            else if (isNew)
                errors.Add("Name is required for a new product.");

            var description = Cell(Col.Description);
            if (description.Length > 1000) errors.Add("Description is longer than 1000 characters.");
            else if (description.Length > 0) row.Description = description;

            var unit = Cell(Col.Unit);
            if (unit.Length > 0)
            {
                row.Unit = Units.FirstOrDefault(u => u.Equals(unit, StringComparison.OrdinalIgnoreCase));
                if (row.Unit == null) errors.Add($"Unit \"{unit}\" is not one of: {string.Join(", ", Units)}.");
            }

            row.Cost = ReadNumber(Cell(Col.PurchasePrice), "PurchasePrice", errors, minimum: 0);
            row.Price = ReadNumber(Cell(Col.SellingPrice), "SellingPrice", errors, minimum: 0, exclusive: true);
            row.Stock = ReadNumber(Cell(Col.Stock), "Stock", errors, minimum: 0);
            row.MinStock = ReadNumber(Cell(Col.MinimumStock), "MinimumStock", errors, minimum: 0);
            if (isNew && row.Price == null && !errors.Any(e => e.StartsWith("SellingPrice")))
                errors.Add("SellingPrice is required for a new product.");

            var active = Cell(Col.Active);
            if (active.Length > 0)
            {
                if (TrueWords.Contains(active, StringComparer.OrdinalIgnoreCase)) row.Active = true;
                else if (FalseWords.Contains(active, StringComparer.OrdinalIgnoreCase)) row.Active = false;
                else errors.Add($"Active \"{active}\" should be Yes or No.");
            }

            var category = Cell(Col.Category);
            if (category.Length > 0)
            {
                if (category.Length > 255) errors.Add("Category is longer than 255 characters.");
                else if (categories.TryGetValue(category, out var found)) row.CategoryId = found.Id;
                else row.NewCategory = category;
            }
            else if (isNew)
                errors.Add("Category is required for a new product.");

            var supplier = Cell(Col.Supplier);
            if (supplier.Length > 0)
            {
                if (suppliers.TryGetValue(supplier, out var found)) row.SupplierId = found.Id;
                else errors.Add($"Supplier \"{supplier}\" does not exist.");
            }
            else if (isNew)
            {
                if (defaultSupplier != null) row.SupplierId = defaultSupplier.Id;
                else errors.Add($"Supplier is empty and there is no \"{DefaultSupplierName}\" supplier to fall back on.");
            }

            if (errors.Count > 0)
            {
                failedRows.Add(rowNo);
                if (row.Barcode.Length > 0) failedBarcodes.Add(row.Barcode);
                Report(null, rowNo, string.Join(" ", errors));
                continue;
            }

            if (row.NewCategory != null)
                newCategories.TryAdd(row.NewCategory, row.NewCategory);
            rows.Add(row);
        }

        // Custom prices: group the rows by product, then hold each product's whole set back if any of its rows is wrong
        // (replacing a product's prices with a partial list would silently drop the rows that failed).
        var rowByBarcode = rows.ToDictionary(r => r.Barcode, StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, PriceGroup>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<(int Row, string Message)>();

        if (priceColumns != null)
        {
            for (var i = 1; i < priceRecords.Count; i++)
            {
                var rowNo = priceRecords[i].Number;
                var record = priceRecords[i].Cells;
                result.TotalPriceRows++;

                string Cell(PriceCol c) => priceColumns.TryGetValue(c, out var at) && at < record.Count ? Clean(record[at]) : "";

                var barcode = Cell(PriceCol.Barcode);
                var priceName = Cell(PriceCol.PriceName);
                var errors = new List<string>();

                PriceGroup? group = null;
                if (barcode.Length == 0)
                    errors.Add("Barcode is empty.");
                else if (MangledNumber.IsMatch(barcode))
                    errors.Add($"Barcode \"{barcode}\" was turned into a number by Excel. Format the Barcode column as Text and enter it again.");
                else if (groups.TryGetValue(barcode, out group)) { }
                else if (rowByBarcode.TryGetValue(barcode, out var target))
                    groups[barcode] = group = new PriceGroup { Row = target, Existing = target.Existing };
                else if (failedBarcodes.Contains(barcode))
                    errors.Add($"The product with barcode \"{barcode}\" on the Products sheet has errors, so its custom prices were skipped.");
                else if (existingByBarcode.TryGetValue(barcode, out var existing) && existing.Count == 1)
                    groups[barcode] = group = new PriceGroup { Existing = existing[0] };
                else
                    errors.Add($"No product has barcode \"{barcode}\". Add it to the Products sheet first.");

                if (priceName.Length == 0) errors.Add("PriceName is empty.");
                else if (priceName.Length > MaxPriceNameLength) errors.Add($"PriceName is longer than {MaxPriceNameLength} characters.");
                else if (group != null && !group.Names.Add(priceName)) errors.Add($"Price name \"{priceName}\" is used twice for this product.");

                var amount = ReadNumber(Cell(PriceCol.Price), "Price", errors, minimum: 0, exclusive: true);
                if (amount == null && !errors.Any(e => e.StartsWith("Price")))
                    errors.Add("Price is required.");
                if (amount != null && group != null)
                {
                    var cost = group.Row != null ? group.Row.Cost ?? group.Existing?.CostPrice : group.Existing?.CostPrice;
                    if (cost != null && amount < cost)
                        errors.Add($"Price {Number(amount)} is below the purchase price ({Number(cost)}).");
                }

                if (errors.Count == 0)
                {
                    group!.Tiers.Add(new ProductPriceInput { Name = priceName, Price = amount!.Value });
                }
                else if (group != null)
                {
                    group.Errors.Add((rowNo, string.Join(" ", errors)));
                }
                else
                {
                    unresolved.Add((rowNo, string.Join(" ", errors)));
                }
            }
        }

        var failedPriceRows = unresolved.Count + groups.Values.Sum(g => g.Errors.Count);
        foreach (var (rowNo, message) in unresolved) Report(PricesSheet, rowNo, message);
        foreach (var group in groups.Values)
            foreach (var (rowNo, message) in group.Errors)
                Report(PricesSheet, rowNo, message + " This product's custom prices were left unchanged.");

        // A product only takes its new set of prices when every one of its rows was fine.
        var existingOnly = new List<(Product Product, List<ProductPriceInput> Tiers)>();
        foreach (var group in groups.Values.Where(g => g.Errors.Count == 0 && g.Tiers.Count > 0))
        {
            if (group.Row != null) group.Row.Tiers = group.Tiers;
            else existingOnly.Add((group.Existing!, group.Tiers));
        }

        result.Failed = failedRows.Count + failedPriceRows;
        result.NewCategories = newCategories.Count;

        // Pass 2: count what changes and, when applying, change it.
        var categoryIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var currencyId = 1;
        if (apply)
        {
            foreach (var name in newCategories.Values)
            {
                var created = await _categories.CreateAsync(new ProductCategory(name));
                categoryIds[name] = created.Id;
            }
            foreach (var row in rows.Where(r => r.NewCategory != null))
            {
                row.CategoryId = categoryIds[row.NewCategory!];
                row.NewCategory = null;
            }
            currencyId = (await _currencies.GetDefaultAsync())?.Id ?? 1;
        }

        var currentTiers = priceColumns == null ? new Dictionary<int, List<ProductPrice>>() : await _prices.GetAllTiersAsync();
        var added = new List<(ParsedRow Row, Product Product)>();
        var tierPlan = new Dictionary<int, List<ProductPriceInput>>();
        var edits = 0;
        var edited = new List<(ProductSnapshot Before, Product After)>();

        foreach (var row in rows)
        {
            if (row.Existing == null)
            {
                result.Created++;
                result.CustomPrices += row.Tiers?.Count ?? 0;
                if (apply) added.Add((row, NewProduct(row, currencyId)));
                continue;
            }

            var changes = Changes(row.Existing, row);
            var pricesChanged = row.Tiers != null && !SameTiers(currentTiers.GetValueOrDefault(row.Existing.Id), row.Tiers);
            if (changes.Count == 0 && !pricesChanged)
            {
                result.Unchanged++;
                continue;
            }

            result.Updated++;
            if (pricesChanged)
            {
                result.CustomPrices += row.Tiers!.Count;
                if (apply) tierPlan[row.Existing.Id] = row.Tiers;
            }
            if (changes.Count > 0 && apply)
            {
                edits++;
                edited.Add((ProductSnapshot.Of(row.Existing), row.Existing));
                foreach (var change in changes) change();
                row.Existing.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Products that only appear on the Custom Prices sheet.
        foreach (var (product, tiers) in existingOnly)
        {
            if (SameTiers(currentTiers.GetValueOrDefault(product.Id), tiers)) continue;
            result.Updated++;
            result.CustomPrices += tiers.Count;
            if (apply) tierPlan[product.Id] = tiers;
        }

        if (apply && (added.Count > 0 || edits > 0 || tierPlan.Count > 0 || added.Any(a => a.Row.Tiers != null)))
        {
            if (added.Count > 0 || edits > 0)
                await _products.SaveRangeAsync(added.Select(a => a.Product));

            // The file's changes are in the database now: write them into each product's history.
            await _history.RecordImportAsync(added.Select(a => a.Product).ToList(), edited);

            foreach (var (row, product) in added.Where(a => a.Row.Tiers != null))
                tierPlan[product.Id] = row.Tiers!;
            if (tierPlan.Count > 0)
                await _prices.ReplaceTiersAsync(tierPlan);

            await _cache.ClearPOSCachesAsync();
        }

        return result;
    }

    private static ProductImportResult Rejected(ProductImportResult result, string reason)
    {
        result.FileError = reason;
        result.Applied = false;
        return result;
    }

    private static bool SameTiers(List<ProductPrice>? current, List<ProductPriceInput> wanted)
    {
        current ??= [];
        return current.Count == wanted.Count
               && current.Zip(wanted).All(p => p.First.Name == p.Second.Name && p.First.Price == p.Second.Price);
    }

    private static Product NewProduct(ParsedRow row, int currencyId) => new()
    {
        Name = row.Name!,
        Description = row.Description,
        Barcode = row.Barcode,
        CostPrice = row.Cost,
        SellingPrice = row.Price!.Value,
        StockQuantity = row.Stock ?? 0,
        MinimumStock = row.MinStock ?? 0,
        BrandId = 1, // Default brand, as in the product form
        CategoryId = row.CategoryId!.Value,
        SupplierId = row.SupplierId!.Value,
        CurrencyId = currencyId,
        IsActive = row.Active ?? true,
        Unit = row.Unit ?? "piece"
    };

    /// <summary>The edits that would turn an existing product into the file's version of it; empty when it already matches.</summary>
    private static List<Action> Changes(Product p, ParsedRow r)
    {
        var changes = new List<Action>();
        if (r.Name != null && r.Name != p.Name) changes.Add(() => p.Name = r.Name);
        if (r.Description != null && r.Description != p.Description) changes.Add(() => p.Description = r.Description);
        if (r.Unit != null && r.Unit != p.Unit) changes.Add(() => p.Unit = r.Unit);
        if (r.Cost != null && r.Cost != p.CostPrice) changes.Add(() => p.CostPrice = r.Cost);
        if (r.Price != null && r.Price != p.SellingPrice) changes.Add(() => p.SellingPrice = r.Price.Value);
        if (r.Stock != null && r.Stock != p.StockQuantity) changes.Add(() => p.StockQuantity = r.Stock.Value);
        if (r.MinStock != null && r.MinStock != p.MinimumStock) changes.Add(() => p.MinimumStock = r.MinStock);
        if (r.Active != null && r.Active != p.IsActive) changes.Add(() => p.IsActive = r.Active.Value);
        if (r.SupplierId != null && r.SupplierId != p.SupplierId) changes.Add(() => p.SupplierId = r.SupplierId.Value);
        // A category that doesn't exist yet always counts as a change; once created (apply) it compares by id.
        if (r.NewCategory != null || (r.CategoryId != null && r.CategoryId != p.CategoryId))
            changes.Add(() => p.CategoryId = r.CategoryId!.Value);
        return changes;
    }

    private static string Number(decimal? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";

    /// <summary>A blank cell is "not given" (null). A bad or out-of-range value adds an error and also yields null.</summary>
    private static decimal? ReadNumber(string cell, string column, List<string> errors, decimal minimum, bool exclusive = false)
    {
        if (cell.Length == 0) return null;
        if (!TryParseNumber(cell, out var value) || value > 1_000_000_000m)
        {
            errors.Add($"{column} \"{cell}\" is not a valid number.");
            return null;
        }
        if (value < minimum || (exclusive && value == minimum))
        {
            errors.Add($"{column} must be {(exclusive ? "greater than" : "at least")} {minimum}.");
            return null;
        }
        return value;
    }

    /// <summary>Reads "12.50", "1,234.50", "1.234,50", "12,5" and Arabic-Indic digits.</summary>
    private static bool TryParseNumber(string raw, out decimal value)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + (ch - '٠')));
            else if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + (ch - '۰')));
            else if (ch == '٫') sb.Append('.');
            else if (ch == '٬') sb.Append(',');
            else if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        }

        var t = sb.ToString();
        var comma = t.LastIndexOf(',');
        var dot = t.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            // Whichever comes last is the decimal mark; the other one groups thousands.
            t = comma > dot ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        }
        else if (comma >= 0)
        {
            // A single comma followed by 1-2 digits is a decimal comma ("12,5"); otherwise it groups thousands ("1,234").
            var decimalComma = t.IndexOf(',') == comma && t.Length - comma - 1 is >= 1 and <= 2;
            t = decimalComma ? t.Replace(',', '.') : t.Replace(",", "");
        }

        return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    private static string Clean(string cell)
    {
        var s = cell.Trim();
        // Undo the apostrophe a CSV export puts in front of text that looks like a formula.
        return s.Length >= 2 && s[0] == '\'' && s[1] is '=' or '+' or '-' or '@' ? s[1..] : s;
    }

    private static Dictionary<Col, int> MapHeader(List<string> header)
    {
        var map = new Dictionary<Col, int>();
        for (var i = 0; i < header.Count; i++)
        {
            if (HeaderAliases.TryGetValue(HeaderKey(header[i]), out var col))
                map.TryAdd(col, i);
        }
        return map;
    }

    private static Dictionary<PriceCol, int> MapPriceHeader(List<string> header)
    {
        var map = new Dictionary<PriceCol, int>();
        for (var i = 0; i < header.Count; i++)
        {
            if (PriceHeaderAliases.TryGetValue(HeaderKey(header[i]), out var col))
                map.TryAdd(col, i);
        }
        return map;
    }

    private static string HeaderKey(string header) =>
        new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    #endregion

    #region Reading the file

    /// <summary>Reads an Excel workbook (Products + Custom Prices sheets) or, failing that, a CSV (products only).</summary>
    private static async Task<(List<SourceRow> Products, List<SourceRow> Prices)> ReadFileAsync(Stream file)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);

        // An .xlsx is a zip archive, which always starts with "PK".
        if (buffer.Length >= 2 && buffer.GetBuffer()[0] == (byte)'P' && buffer.GetBuffer()[1] == (byte)'K')
        {
            buffer.Position = 0;
            return ReadWorkbook(buffer);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync();

        if (text.Contains('�'))
            throw new FormatException("The file is not UTF-8. In Excel use Save As > \"CSV UTF-8 (Comma delimited)\", or save it as an Excel workbook (.xlsx).");

        var records = ParseCsv(text).Select((cells, i) => new SourceRow(i + 1, cells)).ToList();
        return (records, []);
    }

    private static (List<SourceRow> Products, List<SourceRow> Prices) ReadWorkbook(Stream stream)
    {
        XLWorkbook book;
        try
        {
            book = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FormatException("The file could not be opened as an Excel workbook (.xlsx).");
        }

        using (book)
        {
            var sheets = book.Worksheets.Where(w => w.Visibility == XLWorksheetVisibility.Visible).ToList();
            var pricesSheet = sheets.FirstOrDefault(w => PriceSheetNames.Contains(HeaderKey(w.Name)));
            var productsSheet = sheets.FirstOrDefault(w => HeaderKey(w.Name) == "products")
                                ?? sheets.FirstOrDefault(w => w != pricesSheet && HeaderKey(w.Name) != "guide");
            if (productsSheet == null)
                throw new FormatException($"The workbook has no \"{ProductsSheet}\" sheet.");

            return (ReadSheet(productsSheet), pricesSheet == null ? [] : ReadSheet(pricesSheet));
        }
    }

    private static List<SourceRow> ReadSheet(IXLWorksheet sheet)
    {
        var rows = new List<SourceRow>();
        var used = sheet.RangeUsed();
        if (used == null) return rows;

        var firstRow = used.RangeAddress.FirstAddress.RowNumber;
        var lastRow = used.RangeAddress.LastAddress.RowNumber;
        var lastColumn = used.RangeAddress.LastAddress.ColumnNumber;

        for (var r = firstRow; r <= lastRow; r++)
        {
            var cells = new List<string>(lastColumn);
            for (var c = 1; c <= lastColumn; c++) cells.Add(ReadCell(sheet.Cell(r, c)));
            if (cells.Any(c => !string.IsNullOrWhiteSpace(c))) rows.Add(new SourceRow(r, cells));
        }
        return rows;
    }

    /// <summary>A cell as text. Numbers keep every digit, so a barcode typed as a number isn't shown as 6.0012E+12.</summary>
    private static string ReadCell(IXLCell cell)
    {
        var value = cell.Value;
        if (value.IsBlank || value.IsError) return "";
        if (value.IsText) return value.GetText();
        if (value.IsNumber) return value.GetNumber().ToString("0.############", CultureInfo.InvariantCulture);
        if (value.IsBoolean) return value.GetBoolean() ? "true" : "false";
        return cell.GetFormattedString();
    }

    /// <summary>
    /// Splits CSV text into rows of cells: quoted cells may contain the delimiter, quotes ("") and
    /// line breaks; blank lines are dropped. The delimiter comes from Excel's "sep=" line or, failing
    /// that, whichever of , ; or tab the header line uses most.
    /// </summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var start = 0;
        char delimiter;
        if (text.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && text.Length > 4 && text[4] is not ('\r' or '\n'))
        {
            delimiter = text[4];
            var lineEnd = text.IndexOfAny(['\r', '\n']);
            start = lineEnd < 0 ? text.Length : lineEnd;
        }
        else
        {
            var lineEnd = text.IndexOfAny(['\r', '\n']);
            var firstLine = lineEnd < 0 ? text : text[..lineEnd];
            delimiter = new[] { ',', ';', '\t' }.MaxBy(d => firstLine.Count(c => c == d));
            if (!firstLine.Contains(delimiter)) delimiter = ',';
        }

        var records = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        void EndRow()
        {
            row.Add(cell.ToString());
            cell.Clear();
            if (row.Any(c => !string.IsNullOrWhiteSpace(c))) records.Add(row);
            row = new List<string>();
        }

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch != '"') cell.Append(ch);
                else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else inQuotes = false;
            }
            else if (ch == '"' && cell.Length == 0) inQuotes = true;
            else if (ch == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRow();
            }
            else cell.Append(ch);
        }

        if (inQuotes)
            throw new FormatException("The file has a quoted cell that never ends (a missing closing quote).");
        if (cell.Length > 0 || row.Count > 0) EndRow();
        return records;
    }

    #endregion
}
