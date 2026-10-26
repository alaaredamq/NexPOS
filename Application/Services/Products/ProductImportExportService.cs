using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Application.DTOs.Products;
using Application.IServices.Base;
using Application.IServices.Products;
using Core.Entities.Products;
using Core.ICache;
using Core.IRepositories.Products;

namespace Application.Services.Products;

public class ProductImportExportService : IProductImportExportService
{
    private const int MaxRows = 20000;
    private const int MaxReportedIssues = 200;
    private const string DefaultSupplierName = "No Supplier";

    /// <summary>The file's columns, in the order they're exported. The names are the export headers.</summary>
    private enum Col { Barcode, Name, Description, Category, Supplier, Unit, PurchasePrice, SellingPrice, Stock, MinimumStock, Active }

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

    private static readonly string[] TrueWords = ["yes", "y", "true", "1", "active", "نعم", "فعال", "نشط"];
    private static readonly string[] FalseWords = ["no", "n", "false", "0", "inactive", "لا", "غير فعال", "غير نشط"];

    // Excel turns long digit strings into 6.00123E+12 and keeps only that on save.
    private static readonly Regex MangledNumber = new(@"^\d+(\.\d+)?E\+\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IProductRepository _products;
    private readonly IProductCategoryService _categories;
    private readonly ISupplierService _suppliers;
    private readonly ICurrencyService _currencies;
    private readonly ICacheService _cache;

    public ProductImportExportService(IProductRepository products, IProductCategoryService categories,
        ISupplierService suppliers, ICurrencyService currencies, ICacheService cache)
    {
        _products = products;
        _categories = categories;
        _suppliers = suppliers;
        _currencies = currencies;
        _cache = cache;
    }

    #region Export

    public async Task<byte[]> ExportAsync()
    {
        var products = (await _products.GetAllAsync())
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var csv = NewCsv();
        foreach (var p in products)
        {
            WriteRow(csv,
                Text(p.Barcode), Text(p.Name), Text(p.Description), Text(p.Category?.Name), Text(p.Supplier?.Name),
                p.Unit ?? "", Number(p.CostPrice), Number(p.SellingPrice), Number(p.StockQuantity),
                Number(p.MinimumStock), p.IsActive ? "Yes" : "No");
        }
        return Encode(csv);
    }

    public byte[] BuildTemplate()
    {
        var csv = NewCsv();
        WriteRow(csv, "6001234567890", "Sample product", "Optional description", "Beverages", DefaultSupplierName,
            "piece", "1.50", "2.25", "100", "10", "Yes");
        return Encode(csv);
    }

    /// <summary>Starts a file with Excel's "sep=," hint (so it splits columns in locales that use ';') and the header row.</summary>
    private static StringBuilder NewCsv()
    {
        var csv = new StringBuilder("sep=,\r\n");
        WriteRow(csv, Enum.GetNames<Col>());
        return csv;
    }

    private static byte[] Encode(StringBuilder csv)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(csv.ToString());
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static void WriteRow(StringBuilder csv, params string[] cells)
    {
        csv.AppendJoin(',', cells.Select(Quote)).Append("\r\n");
    }

    private static string Quote(string cell)
    {
        if (cell.Length == 0) return "";
        var needsQuotes = cell.AsSpan().IndexOfAny(",\"\r\n") >= 0 || cell[0] == ' ' || cell[^1] == ' ';
        return needsQuotes ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
    }

    /// <summary>Text a spreadsheet would run as a formula ("=...", "+...") gets a leading apostrophe; import takes it off again.</summary>
    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    private static string Number(decimal? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";

    #endregion

    #region Import

    /// <summary>One file row that passed validation. Blank cells are null and leave an existing product's value alone.</summary>
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
    }

    public async Task<ProductImportResult> ImportAsync(Stream csv, bool apply)
    {
        var result = new ProductImportResult { Applied = apply };

        string text;
        using (var reader = new StreamReader(csv, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            text = await reader.ReadToEndAsync();

        if (text.Contains('�'))
            return Rejected(result, "The file is not UTF-8. In Excel use Save As > \"CSV UTF-8 (Comma delimited)\".");

        List<List<string>> records;
        try
        {
            records = ParseCsv(text);
        }
        catch (FormatException ex)
        {
            return Rejected(result, ex.Message);
        }

        if (records.Count == 0)
            return Rejected(result, "The file is empty.");

        var columns = MapHeader(records[0]);
        if (!columns.ContainsKey(Col.Barcode))
            return Rejected(result, "The first row must be a header with at least a \"Barcode\" column.");
        if (records.Count - 1 > MaxRows)
            return Rejected(result, $"The file has more than {MaxRows} rows. Split it into smaller files.");

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
        var barcodeRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var newCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < records.Count; i++)
        {
            var rowNo = i + 1;
            var record = records[i];
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
                if (result.Issues.Count < MaxReportedIssues)
                    result.Issues.Add(new ProductImportIssue(rowNo, string.Join(" ", errors)));
                continue;
            }

            if (row.NewCategory != null)
                newCategories.TryAdd(row.NewCategory, row.NewCategory);
            rows.Add(row);
        }

        result.Failed = failedRows.Count;
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

        var added = new List<Product>();
        foreach (var row in rows)
        {
            if (row.Existing == null)
            {
                result.Created++;
                if (apply) added.Add(NewProduct(row, currencyId));
                continue;
            }

            var changes = Changes(row.Existing, row);
            if (changes.Count == 0)
            {
                result.Unchanged++;
                continue;
            }

            result.Updated++;
            if (apply)
            {
                foreach (var change in changes) change();
                row.Existing.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (apply && (added.Count > 0 || result.Updated > 0))
        {
            await _products.SaveRangeAsync(added);
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
        // Undo the apostrophe the export puts in front of text that looks like a formula.
        return s.Length >= 2 && s[0] == '\'' && s[1] is '=' or '+' or '-' or '@' ? s[1..] : s;
    }

    private static Dictionary<Col, int> MapHeader(List<string> header)
    {
        var map = new Dictionary<Col, int>();
        for (var i = 0; i < header.Count; i++)
        {
            var key = new string(header[i].Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (HeaderAliases.TryGetValue(key, out var col))
                map.TryAdd(col, i);
        }
        return map;
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
