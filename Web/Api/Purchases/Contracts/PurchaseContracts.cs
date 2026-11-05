using System.ComponentModel.DataAnnotations;

namespace Web.Api.Purchases.Contracts;

// Contract of the purchase note feature. Money is in the supplier's currency (CurrencyNotation); a purchase never mixes currencies.

/// <summary>Status of a purchase.</summary>
public static class PurchaseStatusNames
{
    /// <summary>Goods received: stock was added and the supplier's balance moved.</summary>
    public const string Received = "received";

    /// <summary>Undone: stock taken back and the purchase removed from the supplier's balance.</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>One row of the purchases list.</summary>
public sealed record PurchaseListItemDto
{
    public required int Id { get; init; }

    /// <summary><c>PUR-000012</c>.</summary>
    public required string Number { get; init; }

    /// <summary>UTC instant; shown as a day.</summary>
    public required DateTime PurchaseDate { get; init; }

    public required int SupplierId { get; init; }

    public required string SupplierName { get; init; }

    /// <summary>The number on the supplier's own invoice or delivery note.</summary>
    public string? SupplierReference { get; init; }

    public required string CurrencyNotation { get; init; }

    public required int ItemCount { get; init; }

    public required decimal Total { get; init; }

    public required decimal AmountPaid { get; init; }

    /// <summary>Total minus what was paid at the time: what went on the supplier's account.</summary>
    public required decimal AmountDue { get; init; }

    /// <summary>See <see cref="PurchaseStatusNames"/>.</summary>
    public required string Status { get; init; }
}

/// <summary>One product line of a purchase.</summary>
public sealed record PurchaseItemDto
{
    public required int Id { get; init; }

    public required int ProductId { get; init; }

    public required string ProductName { get; init; }

    public string? Barcode { get; init; }

    public string? Unit { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitCost { get; init; }

    public required decimal LineTotal { get; init; }

    /// <summary>This line also set the product's cost price.</summary>
    public required bool UpdatedCostPrice { get; init; }
}

/// <summary>A purchase with its lines: the screen and the receipt.</summary>
public sealed record PurchaseDetailDto
{
    public required int Id { get; init; }

    public required string Number { get; init; }

    public required DateTime PurchaseDate { get; init; }

    public required int SupplierId { get; init; }

    public required string SupplierName { get; init; }

    public string? SupplierPhone { get; init; }

    public string? SupplierEmail { get; init; }

    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    public string? SupplierReference { get; init; }

    public string? Notes { get; init; }

    public required decimal Subtotal { get; init; }

    public required decimal DiscountPercent { get; init; }

    public required decimal DiscountAmount { get; init; }

    public required decimal Total { get; init; }

    public required decimal AmountPaid { get; init; }

    public required decimal AmountDue { get; init; }

    /// <summary>What was paid came out of the cash drawer (it is a cash drop on the drawer session).</summary>
    public required bool PaidFromDrawer { get; init; }

    public required string Status { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>Who entered it.</summary>
    public string? CreatedBy { get; init; }

    public DateTime? CancelledAt { get; init; }

    public string? CancelledBy { get; init; }

    public string? CancelReason { get; init; }

    public required IReadOnlyList<PurchaseItemDto> Items { get; init; }
}

/// <summary>One product line of a new purchase.</summary>
public sealed record PurchaseLineRequest
{
    [Range(1, int.MaxValue)]
    public required int ProductId { get; init; }

    /// <summary>In the product's unit; above zero.</summary>
    [Range(0.001, 1_000_000_000)]
    public required decimal Quantity { get; init; }

    /// <summary>Per unit, in the supplier's currency.</summary>
    [Range(0, 1_000_000_000_000)]
    public required decimal UnitCost { get; init; }

    /// <summary>Also make this the product's cost price (default: yes).</summary>
    public bool UpdateCostPrice { get; init; } = true;
}

/// <summary>Body of a new purchase ("goods received").</summary>
public sealed record PurchaseRequest
{
    [Range(1, int.MaxValue)]
    public required int SupplierId { get; init; }

    /// <summary>The local day the goods arrived (<c>yyyy-MM-dd</c>); defaults to today.</summary>
    public DateOnly? PurchaseDate { get; init; }

    [StringLength(100)]
    public string? SupplierReference { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }

    /// <summary>Percent off the lines' subtotal, 0 to 100.</summary>
    [Range(0, 100)]
    public decimal DiscountPercent { get; init; }

    /// <summary>Paid to the supplier now. The rest is added to what the shop owes them.</summary>
    [Range(0, 1_000_000_000_000)]
    public decimal AmountPaid { get; init; }

    /// <summary>Take the paid amount out of the cash drawer (a cash drop). 409 <c>drawer_not_open</c> when the signed-in user has no open drawer.</summary>
    public bool PayFromDrawer { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(500)]
    public required IReadOnlyList<PurchaseLineRequest> Lines { get; init; }
}

/// <summary>Body of a cancellation.</summary>
public sealed record PurchaseCancelRequest
{
    [StringLength(500)]
    public string? Reason { get; init; }
}

/// <summary>Totals of one currency in the tiles.</summary>
public sealed record PurchaseCurrencyTotalDto
{
    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    public required decimal Total { get; init; }

    public required decimal Paid { get; init; }

    public required decimal Due { get; init; }
}

/// <summary>The tiles above the list; follows the list's supplier, date and search filters but always counts both statuses.</summary>
public sealed record PurchaseSummaryDto
{
    /// <summary>Received purchases.</summary>
    public required int Count { get; init; }

    public required int CancelledCount { get; init; }

    /// <summary>Received purchases only, one entry per currency.</summary>
    public required IReadOnlyList<PurchaseCurrencyTotalDto> Totals { get; init; }
}

/// <summary>Answer of the print endpoint: the printer being unavailable is an answer, not an error.</summary>
public sealed record PurchasePrintResultDto
{
    public required bool Printed { get; init; }

    public required string Message { get; init; }

    public required string PdfUrl { get; init; }
}
