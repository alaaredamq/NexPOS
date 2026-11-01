using Application.IServices.Base;
using Application.IServices.Products;
using Application.IServices.Sales;
using Application.Services.Printing;
using Core.Auditing;
using Core.Entities.Logging;
using Core.Entities.Products;
using Core.Printing;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Inventory.Contracts;
using static Web.Helpers.CurrencyHelper;

namespace Web.Api.Inventory;

/// <summary>The two printable inventory reports.</summary>
public enum InventoryReport
{
    LowStock,
    StockValue,
}

public interface IInventoryApiService
{
    Task<PagedResult<InventoryItemDto>> ListAsync(PagedQuery query);

    Task<InventorySummaryDto> SummaryAsync();

    /// <summary>Null when the product does not exist. Throws <see cref="InsufficientStockException"/> when a removal would take stock below zero.</summary>
    Task<StockAdjustmentResultDto?> AdjustAsync(int productId, StockAdjustmentRequest request);

    /// <summary>Null when the product does not exist.</summary>
    Task<InventoryItemDto?> SetMinimumAsync(int productId, decimal minimumStock);

    Task<LowStockReportDto> LowStockReportAsync(PagedQuery query);

    Task<StockValueReportDto> StockValueReportAsync(PagedQuery query);

    Task<InventoryPrintResultDto> PrintAsync(InventoryReport report);

    Task<(byte[] Pdf, string FileName)> PdfAsync(InventoryReport report);
}

/// <summary>A removal asked for more units than the product has.</summary>
public sealed class InsufficientStockException : Exception
{
    public InsufficientStockException(decimal available, decimal requested)
        : base($"Only {available:0.####} in stock, cannot remove {requested:0.####}.")
    {
        Available = available;
        Requested = requested;
    }

    public decimal Available { get; }

    public decimal Requested { get; }
}

/// <summary>
/// Reads and adjusts stock through the shared product service, so the rules (POS cache clearing, soft delete filter)
/// stay where the Razor page had them. Every figure is the one the old page computed.
/// </summary>
public sealed class InventoryApiService : IInventoryApiService
{
    private static readonly string[] ListSorts = { "name", "barcode", "category", "stock", "minimum", "maximum", "cost", "price", "value", "status" };
    private static readonly string[] ValueSorts = { "value", "name", "category", "stock", "cost" };

    private readonly IProductService _products;
    private readonly ICurrencyService _currencies;
    private readonly IThermalPrinterService _printer;
    private readonly IAuditTrail _audit;
    private readonly IStringLocalizer<SharedResource> _text;

    public InventoryApiService(
        IProductService products,
        ICurrencyService currencies,
        IThermalPrinterService printer,
        IAuditTrail audit,
        IStringLocalizer<SharedResource> text)
    {
        _products = products;
        _currencies = currencies;
        _printer = printer;
        _audit = audit;
        _text = text;
    }

    // ---- list and tiles ----------------------------------------------------------------------------------------

    public async Task<PagedResult<InventoryItemDto>> ListAsync(PagedQuery query)
    {
        var products = await _products.GetAllAsync();
        var notation = await DefaultNotationAsync();

        IEnumerable<Product> filtered = products;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            filtered = filtered.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (p.Description ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (p.Category?.Name ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered.Select(p => ToItem(p, notation));
        return PagedResult<InventoryItemDto>.From(SortList(rows, query), query);
    }

    public async Task<InventorySummaryDto> SummaryAsync()
    {
        var products = await _products.GetAllAsync();
        var notation = await DefaultNotationAsync();

        return new InventorySummaryDto
        {
            TotalProducts = products.Count,
            LowStockCount = products.Count(IsAtOrBelowMinimum),
            OutOfStockCount = products.Count(p => p.StockQuantity <= 0),
            TotalStockValue = products.Sum(UnitValue),
            CurrencyNotation = notation,
        };
    }

    // ---- changes -----------------------------------------------------------------------------------------------

    public async Task<StockAdjustmentResultDto?> AdjustAsync(int productId, StockAdjustmentRequest request)
    {
        var product = await FindAsync(productId);
        if (product is null) return null;

        var previous = product.StockQuantity;
        var add = string.Equals(request.Type, "add", StringComparison.OrdinalIgnoreCase);
        var next = add ? previous + request.Quantity : previous - request.Quantity;
        if (next < 0) throw new InsufficientStockException(previous, request.Quantity);

        product.StockQuantity = next;
        await _products.UpdateAsync(productId, product);

        _audit.LogBusiness(
            AuditActions.StockAdjusted,
            $"Stock of '{product.Name}' {(add ? "increased" : "decreased")} by {request.Quantity:0.####}: {previous:0.####} to {next:0.####}",
            "Product",
            entityId: productId,
            entityLabel: product.Name,
            details: new { productId, type = add ? "add" : "remove", quantity = request.Quantity, previous, next, reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim() });

        var notation = await DefaultNotationAsync();
        var fresh = await FindAsync(productId) ?? product;
        return new StockAdjustmentResultDto { PreviousStock = previous, NewStock = next, Item = ToItem(fresh, notation) };
    }

    public async Task<InventoryItemDto?> SetMinimumAsync(int productId, decimal minimumStock)
    {
        var product = await FindAsync(productId);
        if (product is null) return null;

        var previous = product.MinimumStock;
        product.MinimumStock = minimumStock;
        await _products.UpdateAsync(productId, product);

        _audit.LogBusiness(
            AuditActions.Update,
            $"Minimum stock of '{product.Name}' set to {minimumStock:0.####}",
            "Product",
            entityId: productId,
            entityLabel: product.Name,
            details: new { productId, previousMinimum = previous, minimumStock });

        return ToItem(await FindAsync(productId) ?? product, await DefaultNotationAsync());
    }

    // ---- reports -----------------------------------------------------------------------------------------------

    public async Task<LowStockReportDto> LowStockReportAsync(PagedQuery query)
    {
        var low = LowStockProducts(await _products.GetAllAsync());

        IEnumerable<Product> filtered = low;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            filtered = filtered.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (p.Category?.Name ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Least stock first, like the old page; the name keeps equal rows from swapping between pages.
        var ordered = filtered.OrderBy(p => p.StockQuantity).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Id);

        return new LowStockReportDto
        {
            Summary = new LowStockSummaryDto
            {
                Products = low.Count,
                OutOfStock = low.Count(p => p.StockQuantity <= 0),
                TotalUnits = low.Sum(p => p.StockQuantity),
            },
            Items = PagedResult<LowStockItemDto>.From(ordered.Select(ToLowStockItem), query),
        };
    }

    public async Task<StockValueReportDto> StockValueReportAsync(PagedQuery query)
    {
        var rows = await StockValueRowsAsync();
        var notation = await DefaultNotationAsync();
        var total = rows.Sum(r => r.Value);

        IEnumerable<(Product Product, decimal UnitCost, decimal Value, string Notation)> filtered = rows;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            filtered = filtered.Where(r =>
                r.Product.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.Product.Category?.Name ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var items = filtered.Select(r => new StockValueItemDto
        {
            Id = r.Product.Id,
            Name = r.Product.Name,
            CategoryName = r.Product.Category?.Name,
            StockQuantity = r.Product.StockQuantity,
            Unit = r.Product.Unit,
            UnitCost = r.UnitCost,
            StockValue = r.Value,
            CurrencyNotation = r.Notation,
            ValuePercent = total > 0 ? Math.Round(r.Value / total * 100, 1) : 0,
        });

        return new StockValueReportDto
        {
            TotalProducts = rows.Count,
            TotalUnits = rows.Sum(r => r.Product.StockQuantity),
            TotalValue = total,
            AverageValue = rows.Count > 0 ? total / rows.Count : 0,
            CurrencyNotation = notation,
            ByCategory = rows
                .GroupBy(r => r.Product.Category?.Name ?? string.Empty)
                .Select(g => new StockValueSliceDto { Label = g.Key, Value = Math.Round(g.Sum(r => r.Value), 2) })
                .OrderByDescending(s => s.Value)
                .Take(8)
                .ToList(),
            TopProducts = rows
                .Take(10)
                .Select(r => new StockValueSliceDto { Label = r.Product.Name, Value = Math.Round(r.Value, 2) })
                .ToList(),
            Items = PagedResult<StockValueItemDto>.From(SortValue(items, query), query),
        };
    }

    // ---- printing ----------------------------------------------------------------------------------------------

    public async Task<InventoryPrintResultDto> PrintAsync(InventoryReport report)
    {
        var document = await BuildDocumentAsync(report);
        var printed = await _printer.PrintDocumentAsync(document, jobName: document.Title);

        // The endpoint answers 200 either way (the page shows the message), so the audit row is what tells "printed" from "printer unavailable".
        _audit.LogBusiness(
            printed ? AuditActions.Print : AuditActions.PrintFailed,
            printed ? $"Sent '{document.Title}' to the receipt printer" : $"Could not print '{document.Title}' - receipt printer unavailable",
            "PrintJob",
            printed ? AuditOutcome.Success : AuditOutcome.Failure,
            printed ? AuditLogSeverity.Info : AuditLogSeverity.Warning,
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, pdfUrl = PdfUrl(report) });

        return new InventoryPrintResultDto
        {
            Printed = printed,
            Message = _text[printed ? "Pos_PrintOk" : "Pos_PrintFailed"].Value,
            PdfUrl = PdfUrl(report),
        };
    }

    public async Task<(byte[] Pdf, string FileName)> PdfAsync(InventoryReport report)
    {
        var document = await BuildDocumentAsync(report);
        var pdf = _printer.GeneratePdf(document, PdfPaper.A4);

        _audit.LogBusiness(AuditActions.PdfGenerated, $"PDF generated: '{document.Title}'", "PrintJob",
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, paper = nameof(PdfPaper.A4), bytes = pdf.Length });
        return (pdf, $"{document.FileName}.pdf");
    }

    private static string PdfUrl(InventoryReport report)
        => report == InventoryReport.LowStock ? "/api/v1/inventory/reports/low-stock.pdf" : "/api/v1/inventory/reports/stock-value.pdf";

    private async Task<PrintDocument> BuildDocumentAsync(InventoryReport report)
    {
        if (report == InventoryReport.LowStock)
        {
            var items = LowStockProducts(await _products.GetAllAsync())
                .OrderBy(p => p.StockQuantity)
                .Select(p => new LowStockItem(p.Name, p.Category?.Name ?? "No Category", p.StockQuantity, p.MinimumStock!.Value))
                .ToList();
            return ReportDocuments.LowStock(items);
        }

        var rows = await StockValueRowsAsync();
        var notation = await DefaultNotationAsync();
        return ReportDocuments.StockValue(
            rows.Select(r => new StockValueItem(
                r.Product.Name,
                r.Product.Category?.Name ?? "No Category",
                r.Product.StockQuantity,
                FormatCurrency(r.UnitCost, r.Notation),
                FormatCurrency(r.Value, r.Notation))).ToList(),
            FormatCurrency(rows.Sum(r => r.Value), notation));
    }

    // ---- shared pieces -----------------------------------------------------------------------------------------

    /// <summary>Active products, most valuable first: the rows of the stock-value report, its charts and its printout.</summary>
    private async Task<List<(Product Product, decimal UnitCost, decimal Value, string Notation)>> StockValueRowsAsync()
    {
        var products = await _products.GetAllAsync();
        var fallback = await DefaultNotationAsync();

        return products
            .Where(p => p.IsActive)
            .Select(p =>
            {
                var unitCost = p.CostPrice ?? p.SellingPrice;
                return (Product: p, UnitCost: unitCost, Value: p.StockQuantity * unitCost, Notation: p.Currency?.Notation ?? fallback);
            })
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.Product.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<Product> LowStockProducts(IEnumerable<Product> products) => products.Where(IsAtOrBelowMinimum).ToList();

    private static bool IsAtOrBelowMinimum(Product p) => p.MinimumStock.HasValue && p.StockQuantity <= p.MinimumStock.Value;

    private static decimal UnitValue(Product p) => p.StockQuantity * (p.CostPrice ?? p.SellingPrice);

    private async Task<string> DefaultNotationAsync() => (await _currencies.GetDefaultAsync())?.Notation ?? "$";

    // The repository throws ArgumentNullException (not null) for an unknown id.
    private async Task<Product?> FindAsync(int id)
    {
        try
        {
            return await _products.GetByIdAsync(id);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    private static string StatusOf(Product p)
        => p.StockQuantity <= 0 ? InventoryStatus.Out : IsAtOrBelowMinimum(p) ? InventoryStatus.Low : InventoryStatus.Ok;

    private static InventoryItemDto ToItem(Product p, string fallbackNotation) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Barcode = p.Barcode,
        CategoryName = p.Category?.Name,
        CurrentStock = p.StockQuantity,
        MinimumStock = p.MinimumStock,
        MaximumStock = p.MaximumStock,
        CostPrice = p.CostPrice,
        SellingPrice = p.SellingPrice,
        CurrencyNotation = p.Currency?.Notation ?? fallbackNotation,
        StockValue = UnitValue(p),
        Unit = p.Unit,
        IsActive = p.IsActive,
        Status = StatusOf(p),
    };

    private static LowStockItemDto ToLowStockItem(Product p)
    {
        var minimum = p.MinimumStock ?? 0;
        return new LowStockItemDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Barcode = p.Barcode,
            CategoryName = p.Category?.Name,
            CurrentStock = p.StockQuantity,
            MinimumStock = minimum,
            Deficit = Math.Max(0, minimum - p.StockQuantity),
            Unit = p.Unit,
            Priority = p.StockQuantity <= 0 ? LowStockPriority.Critical : p.StockQuantity <= minimum * 0.5m ? LowStockPriority.High : LowStockPriority.Medium,
        };
    }

    private static IEnumerable<InventoryItemDto> SortList(IEnumerable<InventoryItemDto> rows, PagedQuery query)
    {
        var column = ListSorts.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "name";
        var desc = query.Descending;

        return column switch
        {
            "barcode" => Order(rows, r => r.Barcode, desc),
            "category" => Order(rows, r => r.CategoryName ?? string.Empty, desc),
            "stock" => Order(rows, r => r.CurrentStock, desc),
            "minimum" => Order(rows, r => r.MinimumStock ?? 0, desc),
            "maximum" => Order(rows, r => r.MaximumStock ?? 0, desc),
            "cost" => Order(rows, r => r.CostPrice ?? 0, desc),
            "price" => Order(rows, r => r.SellingPrice, desc),
            "value" => Order(rows, r => r.StockValue, desc),
            "status" => Order(rows, r => r.Status == InventoryStatus.Out ? 0 : r.Status == InventoryStatus.Low ? 1 : 2, desc),
            _ => Order(rows, r => r.Name, desc),
        };

        static IEnumerable<InventoryItemDto> Order<TKey>(IEnumerable<InventoryItemDto> source, Func<InventoryItemDto, TKey> key, bool descending)
        {
            var ordered = descending ? source.OrderByDescending(key, Comparer<TKey>.Default) : source.OrderBy(key, Comparer<TKey>.Default);
            return ordered.ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Id);
        }
    }

    private static IEnumerable<StockValueItemDto> SortValue(IEnumerable<StockValueItemDto> rows, PagedQuery query)
    {
        // Most valuable first unless asked otherwise (the old table's initial order).
        var column = ValueSorts.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "value";
        var desc = string.IsNullOrEmpty(query.Sort) && string.IsNullOrEmpty(query.Dir) ? true : query.Descending;

        return column switch
        {
            "name" => Order(rows, r => r.Name, desc),
            "category" => Order(rows, r => r.CategoryName ?? string.Empty, desc),
            "stock" => Order(rows, r => r.StockQuantity, desc),
            "cost" => Order(rows, r => r.UnitCost, desc),
            _ => Order(rows, r => r.StockValue, desc),
        };

        static IEnumerable<StockValueItemDto> Order<TKey>(IEnumerable<StockValueItemDto> source, Func<StockValueItemDto, TKey> key, bool descending)
        {
            var ordered = descending ? source.OrderByDescending(key, Comparer<TKey>.Default) : source.OrderBy(key, Comparer<TKey>.Default);
            return ordered.ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Id);
        }
    }
}
