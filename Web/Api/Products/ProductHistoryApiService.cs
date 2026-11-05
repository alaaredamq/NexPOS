using System.Globalization;
using Application.IServices.Products;
using Core.Entities.Products;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Infrastructure;
using Web.Api.Products.Contracts;

namespace Web.Api.Products;

/// <summary>Which lines of the history to show.</summary>
public sealed class ProductHistoryFilter
{
    /// <summary><c>stock</c> (opening stock, adds, removals, corrections, purchases), <c>sales</c> (sold / returned) or <c>changes</c> (prices, details, delete); anything else = all.</summary>
    [FromQuery(Name = "kind")]
    public string? Kind { get; set; }
}

public interface IProductHistoryApiService
{
    /// <summary>Null when the product does not exist.</summary>
    Task<ProductHistoryDto?> GetAsync(int productId, PagedQuery query, ProductHistoryFilter filter);
}

public sealed class ProductHistoryApiService : IProductHistoryApiService
{
    private static readonly ProductHistoryType[] StockKinds =
    [
        ProductHistoryType.Created, ProductHistoryType.StockAdded, ProductHistoryType.StockRemoved,
        ProductHistoryType.StockSet, ProductHistoryType.Received,
    ];

    private static readonly ProductHistoryType[] SalesKinds = [ProductHistoryType.Sold, ProductHistoryType.SaleReturned];

    private static readonly ProductHistoryType[] ChangeKinds =
    [
        ProductHistoryType.SellingPriceChanged, ProductHistoryType.CostPriceChanged, ProductHistoryType.MinimumStockChanged,
        ProductHistoryType.Updated, ProductHistoryType.Deleted,
    ];

    private readonly IProductService _products;
    private readonly IProductHistoryService _history;

    public ProductHistoryApiService(IProductService products, IProductHistoryService history)
    {
        _products = products;
        _history = history;
    }

    public async Task<ProductHistoryDto?> GetAsync(int productId, PagedQuery query, ProductHistoryFilter filter)
    {
        var product = await FindAsync(productId);
        if (product is null) return null;

        var types = (filter.Kind?.Trim().ToLowerInvariant()) switch
        {
            "stock" => StockKinds,
            "sales" => SalesKinds,
            "changes" => ChangeKinds,
            _ => null,
        };

        var page = await _history.GetPageAsync(productId, types, query.Page, query.PageSize);

        return new ProductHistoryDto
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Unit = product.Unit,
            CurrencyNotation = product.Currency?.Notation ?? "",
            CurrentStock = product.StockQuantity,
            TotalSold = page.Totals.Sold,
            TotalReturned = page.Totals.Returned,
            TotalAdded = page.Totals.Added,
            Items = PagedResult<ProductHistoryItemDto>.Of(page.Items.Select(ToItem).ToList(), page.Total, query),
        };
    }

    private static ProductHistoryItemDto ToItem(ProductHistory h) => new()
    {
        Id = h.Id,
        CreatedAt = h.CreatedAt,
        Type = KindOf(h.Type),
        QuantityChange = h.QuantityChange,
        StockBefore = h.StockBefore,
        StockAfter = h.StockAfter,
        OrderId = h.OrderId,
        OrderNumber = h.OrderId is { } order ? $"ORD-{order:D6}" : null,
        PurchaseId = h.PurchaseId,
        Reason = h.Reason,
        OldValue = Number(h.OldValue),
        NewValue = Number(h.NewValue),
        Fields = h.Type == ProductHistoryType.Updated && !string.IsNullOrEmpty(h.Details)
            ? h.Details.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : [],
        FromImport = h.Details == "import",
        UserName = h.UserName,
    };

    private static string KindOf(ProductHistoryType type) => type switch
    {
        ProductHistoryType.Created => ProductHistoryKind.Created,
        ProductHistoryType.StockAdded => ProductHistoryKind.StockAdded,
        ProductHistoryType.StockRemoved => ProductHistoryKind.StockRemoved,
        ProductHistoryType.StockSet => ProductHistoryKind.StockSet,
        ProductHistoryType.Sold => ProductHistoryKind.Sold,
        ProductHistoryType.SaleReturned => ProductHistoryKind.SaleReturned,
        ProductHistoryType.SellingPriceChanged => ProductHistoryKind.SellingPriceChanged,
        ProductHistoryType.CostPriceChanged => ProductHistoryKind.CostPriceChanged,
        ProductHistoryType.MinimumStockChanged => ProductHistoryKind.MinimumStockChanged,
        ProductHistoryType.Updated => ProductHistoryKind.Updated,
        ProductHistoryType.Deleted => ProductHistoryKind.Deleted,
        ProductHistoryType.Received => ProductHistoryKind.Received,
        _ => ProductHistoryKind.Updated,
    };

    private static decimal? Number(string? text)
        => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    // The repository throws ArgumentNullException (not null) for an unknown id.
    private async Task<Product?> FindAsync(int id)
    {
        if (id <= 0) return null;
        try
        {
            return await _products.GetByIdAsync(id);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }
}
