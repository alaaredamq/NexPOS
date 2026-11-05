using Web.Api.Infrastructure;

namespace Web.Api.Products.Contracts;

/// <summary>What happened to a product, as the history page words it.</summary>
public static class ProductHistoryKind
{
    public const string Created = "created";
    public const string StockAdded = "stockAdded";
    public const string StockRemoved = "stockRemoved";
    public const string StockSet = "stockSet";
    public const string Sold = "sold";
    public const string SaleReturned = "saleReturned";
    public const string SellingPriceChanged = "sellingPriceChanged";
    public const string CostPriceChanged = "costPriceChanged";
    public const string MinimumStockChanged = "minimumStockChanged";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
    public const string Received = "received";
}

/// <summary>One line of a product's history. The page builds the sentence from these fields, in the user's language.</summary>
public sealed record ProductHistoryItemDto
{
    public required int Id { get; init; }

    /// <summary>When it happened (UTC).</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>One of <see cref="ProductHistoryKind"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Signed units moved: negative took stock away. Null when the event did not touch stock.</summary>
    public decimal? QuantityChange { get; init; }

    public decimal? StockBefore { get; init; }

    public decimal? StockAfter { get; init; }

    public int? OrderId { get; init; }

    /// <summary><c>ORD-000042</c>, for a sale or a return.</summary>
    public string? OrderNumber { get; init; }

    public int? PurchaseId { get; init; }

    /// <summary>What the person typed as the reason.</summary>
    public string? Reason { get; init; }

    /// <summary>Before / after of a price or minimum-stock change; null when there was no value (no purchase price set).</summary>
    public decimal? OldValue { get; init; }

    public decimal? NewValue { get; init; }

    /// <summary>For <c>updated</c>: which fields changed (<c>name</c>, <c>description</c>, <c>barcode</c>, <c>category</c>, <c>supplier</c>, <c>unit</c>, <c>activated</c>, <c>deactivated</c>).</summary>
    public required IReadOnlyList<string> Fields { get; init; }

    /// <summary>The line came from an import file.</summary>
    public required bool FromImport { get; init; }

    /// <summary>Who did it; null for work nobody was signed in for.</summary>
    public string? UserName { get; init; }
}

/// <summary>A product's history: the product, lifetime totals and one page of lines, newest first.</summary>
public sealed record ProductHistoryDto
{
    public required int ProductId { get; init; }

    public required string ProductName { get; init; }

    public string? Unit { get; init; }

    /// <summary>The product's currency, for the price lines (empty when it has none).</summary>
    public required string CurrencyNotation { get; init; }

    public required decimal CurrentStock { get; init; }

    /// <summary>Units sold over the product's life (returns not taken off).</summary>
    public required decimal TotalSold { get; init; }

    /// <summary>Units that came back from cancelled sales.</summary>
    public required decimal TotalReturned { get; init; }

    /// <summary>Units added by hand or received from suppliers.</summary>
    public required decimal TotalAdded { get; init; }

    public required PagedResult<ProductHistoryItemDto> Items { get; init; }
}
