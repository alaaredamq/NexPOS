using System.ComponentModel.DataAnnotations;
using Web.Api.Infrastructure;

namespace Web.Api.Inventory.Contracts;

// Contract of track C3 (Inventory). Mirrors the Razor InventoryController; see docs/react-migration/notes/C3.md for the differences.

/// <summary>The status of a stock figure, by the rule the old Inventory page used.</summary>
public static class InventoryStatus
{
    /// <summary>Stock is zero or below.</summary>
    public const string Out = "out";

    /// <summary>A minimum is set and stock is at or below it (and above zero).</summary>
    public const string Low = "low";

    public const string Ok = "ok";
}

/// <summary>How urgent a low-stock product is.</summary>
public static class LowStockPriority
{
    /// <summary>Out of stock.</summary>
    public const string Critical = "critical";

    /// <summary>At or below half of the minimum.</summary>
    public const string High = "high";

    public const string Medium = "medium";
}

/// <summary>One row of the inventory list.</summary>
public sealed record InventoryItemDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string Barcode { get; init; }

    /// <summary>Null when the product has no category; the UI shows its own "No category" text.</summary>
    public string? CategoryName { get; init; }

    public required decimal CurrentStock { get; init; }

    /// <summary>Null when no minimum was ever set.</summary>
    public decimal? MinimumStock { get; init; }

    public decimal? MaximumStock { get; init; }

    /// <summary>Null when no cost price is recorded (stock value then uses the selling price).</summary>
    public decimal? CostPrice { get; init; }

    public required decimal SellingPrice { get; init; }

    /// <summary>Notation of the product's own currency (<c>$</c>, <c>L.L.</c>).</summary>
    public required string CurrencyNotation { get; init; }

    /// <summary>Stock x (cost price, or selling price when there is no cost), in the product's own currency (no conversion, as before).</summary>
    public required decimal StockValue { get; init; }

    /// <summary>The product's unit (<c>kg</c>, <c>piece</c>) or null.</summary>
    public string? Unit { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>See <see cref="InventoryStatus"/>.</summary>
    public required string Status { get; init; }
}

/// <summary>The four tiles above the inventory list.</summary>
public sealed record InventorySummaryDto
{
    public required int TotalProducts { get; init; }

    /// <summary>Products with a minimum set and stock at or below it (out-of-stock ones with a minimum are included, as before).</summary>
    public required int LowStockCount { get; init; }

    /// <summary>Products with stock of zero or below.</summary>
    public required int OutOfStockCount { get; init; }

    /// <summary>Stock x cost over every product, active or not, in the default currency's notation (the old figure; no conversion).</summary>
    public required decimal TotalStockValue { get; init; }

    /// <summary>Notation of the default currency, for <see cref="TotalStockValue"/>.</summary>
    public required string CurrencyNotation { get; init; }
}

/// <summary>Body of an adjustment.</summary>
public sealed record StockAdjustmentRequest
{
    /// <summary><c>add</c> or <c>remove</c>.</summary>
    [Required]
    [RegularExpression("(?i)^(add|remove)$")]
    public required string Type { get; init; }

    /// <summary>Units to add or remove, in the product's unit; decimals allowed (kg, litres).</summary>
    [Range(0.0001, 1_000_000_000)]
    public required decimal Quantity { get; init; }

    /// <summary>Why; kept in the audit trail.</summary>
    [StringLength(500)]
    public string? Reason { get; init; }
}

/// <summary>What an adjustment did.</summary>
public sealed record StockAdjustmentResultDto
{
    public required decimal PreviousStock { get; init; }

    public required decimal NewStock { get; init; }

    public required InventoryItemDto Item { get; init; }
}

/// <summary>Body of "set minimum stock".</summary>
public sealed record MinimumStockRequest
{
    [Range(0, 1_000_000_000)]
    public required decimal MinimumStock { get; init; }
}

/// <summary>The numbers above the low-stock report.</summary>
public sealed record LowStockSummaryDto
{
    /// <summary>Products with low stock (out-of-stock included).</summary>
    public required int Products { get; init; }

    public required int OutOfStock { get; init; }

    /// <summary>Units still on the shelf across those products.</summary>
    public required decimal TotalUnits { get; init; }
}

/// <summary>One product of the low-stock report.</summary>
public sealed record LowStockItemDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string Barcode { get; init; }

    public string? CategoryName { get; init; }

    public required decimal CurrentStock { get; init; }

    public required decimal MinimumStock { get; init; }

    /// <summary>How far under the minimum the stock is (never negative).</summary>
    public required decimal Deficit { get; init; }

    public string? Unit { get; init; }

    /// <summary>See <see cref="LowStockPriority"/>.</summary>
    public required string Priority { get; init; }
}

/// <summary>The low-stock report: totals plus one page of products, least stock first.</summary>
public sealed record LowStockReportDto
{
    public required LowStockSummaryDto Summary { get; init; }

    public required PagedResult<LowStockItemDto> Items { get; init; }
}

/// <summary>One bar of the stock-value charts. An empty label stands for products with no category.</summary>
public sealed record StockValueSliceDto
{
    public required string Label { get; init; }

    public required decimal Value { get; init; }
}

/// <summary>One product of the stock-value report.</summary>
public sealed record StockValueItemDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? CategoryName { get; init; }

    public required decimal StockQuantity { get; init; }

    public string? Unit { get; init; }

    /// <summary>Cost price, or the selling price when there is no cost.</summary>
    public required decimal UnitCost { get; init; }

    public required decimal StockValue { get; init; }

    /// <summary>Notation of the product's own currency.</summary>
    public required string CurrencyNotation { get; init; }

    /// <summary>Share of the report's total value, 0 to 100.</summary>
    public required decimal ValuePercent { get; init; }
}

/// <summary>The stock-value report over active products.</summary>
public sealed record StockValueReportDto
{
    public required int TotalProducts { get; init; }

    public required decimal TotalUnits { get; init; }

    /// <summary>Sum of every row's value, in the default currency's notation (no conversion, as before).</summary>
    public required decimal TotalValue { get; init; }

    public required decimal AverageValue { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>The eight categories with the most value (the old pie chart). A product with no category counts under an empty label.</summary>
    public required IReadOnlyList<StockValueSliceDto> ByCategory { get; init; }

    /// <summary>The ten most valuable products (the old bar chart).</summary>
    public required IReadOnlyList<StockValueSliceDto> TopProducts { get; init; }

    public required PagedResult<StockValueItemDto> Items { get; init; }
}

/// <summary>Answer of "print on the receipt printer". A printer problem is not an HTTP error: the page shows the message and offers the PDF.</summary>
public sealed record InventoryPrintResultDto
{
    public required bool Printed { get; init; }

    /// <summary>Already localized.</summary>
    public required string Message { get; init; }

    /// <summary>Where the same document can be downloaded as a PDF.</summary>
    public required string PdfUrl { get; init; }
}
