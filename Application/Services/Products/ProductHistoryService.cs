using System.Globalization;
using Application.IServices.Products;
using Core.Auditing;
using Core.Entities.Products;
using Core.IRepositories.Products;
using Microsoft.Extensions.Logging;

namespace Application.Services.Products;

public class ProductHistoryService : IProductHistoryService
{
    /// <summary>Marks a line that came from an import file (the page words it as "by import").</summary>
    public const string ImportMarker = "import";

    private readonly IProductHistoryRepository _history;
    private readonly IProductRepository _products;
    private readonly IAuditContext _actor;
    private readonly ILogger<ProductHistoryService> _logger;

    public ProductHistoryService(
        IProductHistoryRepository history,
        IProductRepository products,
        IAuditContext actor,
        ILogger<ProductHistoryService> logger)
    {
        _history = history;
        _products = products;
        _actor = actor;
        _logger = logger;
    }

    public Task RecordCreatedAsync(Product product, bool fromImport = false) => SaveAsync([CreatedLine(product, fromImport)]);

    public Task RecordDeletedAsync(int productId) => SaveAsync([New(productId, ProductHistoryType.Deleted)]);

    public Task RecordImportAsync(IReadOnlyList<Product> created, IReadOnlyList<(ProductSnapshot Before, Product After)> changed)
    {
        var lines = created.Select(p => CreatedLine(p, fromImport: true)).ToList();
        var context = new ProductChangeContext(FromImport: true);
        foreach (var (before, after) in changed)
            lines.AddRange(ChangeLines(before, after, context));
        return SaveAsync(lines);
    }

    public Task RecordChangesAsync(ProductSnapshot before, Product after, ProductChangeContext? context = null)
        => SaveAsync(ChangeLines(before, after, context));

    private ProductHistory CreatedLine(Product product, bool fromImport) => New(product.Id, ProductHistoryType.Created, entry =>
    {
        entry.StockAfter = product.StockQuantity;
        entry.Details = fromImport ? ImportMarker : null;
    });

    private List<ProductHistory> ChangeLines(ProductSnapshot before, Product after, ProductChangeContext? context)
    {
        var lines = new List<ProductHistory>();

        if (before.StockQuantity != after.StockQuantity)
        {
            var change = after.StockQuantity - before.StockQuantity;
            // "Added / removed" only makes sense for a hand adjustment; a typed-in or imported figure is "set".
            var type = context?.StockType ?? ProductHistoryType.StockSet;
            lines.Add(New(after.Id, type, entry =>
            {
                entry.QuantityChange = change;
                entry.StockBefore = before.StockQuantity;
                entry.StockAfter = after.StockQuantity;
                entry.Reason = Clean(context?.Reason);
                if (context?.FromImport == true) entry.Details = ImportMarker;
            }));
        }

        if (before.SellingPrice != after.SellingPrice)
            lines.Add(Change(after.Id, ProductHistoryType.SellingPriceChanged, before.SellingPrice, after.SellingPrice));

        if (before.CostPrice != after.CostPrice)
            lines.Add(Change(after.Id, ProductHistoryType.CostPriceChanged, before.CostPrice, after.CostPrice));

        if (before.MinimumStock != after.MinimumStock)
            lines.Add(Change(after.Id, ProductHistoryType.MinimumStockChanged, before.MinimumStock, after.MinimumStock));

        var fields = new List<string>();
        if (before.Name != after.Name) fields.Add("name");
        if ((before.Description ?? "") != (after.Description ?? "")) fields.Add("description");
        if (before.Barcode != after.Barcode) fields.Add("barcode");
        if (before.CategoryId != after.CategoryId) fields.Add("category");
        if (before.SupplierId != after.SupplierId) fields.Add("supplier");
        if (before.IsActive != after.IsActive) fields.Add(after.IsActive ? "activated" : "deactivated");
        if ((before.Unit ?? "") != (after.Unit ?? "")) fields.Add("unit");
        if (fields.Count > 0)
            lines.Add(New(after.Id, ProductHistoryType.Updated, entry => entry.Details = string.Join(',', fields)));

        return lines;
    }

    public async Task RecordMovementAsync(int productId, ProductHistoryType type, decimal change, int? orderId = null,
        string? reason = null, int? purchaseId = null, decimal? stockBefore = null, decimal? stockAfter = null)
    {
        try
        {
            var after = stockAfter ?? await _products.GetStockQuantityAsync(productId);
            await SaveAsync([
                New(productId, type, entry =>
                {
                    entry.QuantityChange = change;
                    entry.StockAfter = after;
                    entry.StockBefore = stockBefore ?? (after - change);
                    entry.OrderId = orderId;
                    entry.PurchaseId = purchaseId;
                    entry.Reason = Clean(reason);
                })
            ]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record {Type} in the history of product {ProductId}", type, productId);
        }
    }

    public async Task<ProductHistoryPage> GetPageAsync(int productId, IReadOnlyCollection<ProductHistoryType>? types, int page, int pageSize)
    {
        var (items, total) = await _history.GetPageAsync(productId, types, (page - 1) * pageSize, pageSize);
        return new ProductHistoryPage(items, total, await _history.GetTotalsAsync(productId));
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private ProductHistory Change(int productId, ProductHistoryType type, decimal? oldValue, decimal? newValue)
        => New(productId, type, entry =>
        {
            entry.OldValue = Number(oldValue);
            entry.NewValue = Number(newValue);
        });

    private ProductHistory New(int productId, ProductHistoryType type, Action<ProductHistory>? fill = null)
    {
        var who = _actor.Capture();
        var entry = new ProductHistory
        {
            ProductId = productId,
            Type = type,
            UserId = who.UserId,
            UserName = who.Username,
        };
        fill?.Invoke(entry);
        return entry;
    }

    private async Task SaveAsync(List<ProductHistory> lines)
    {
        if (lines.Count == 0) return;

        try
        {
            await _history.AddRangeAsync(lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record {Count} line(s) in the history of product {ProductId}", lines.Count, lines[0].ProductId);
        }
    }

    private static string? Number(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture);

    private static string? Clean(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }
}
