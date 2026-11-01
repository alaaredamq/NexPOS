using System.ComponentModel.DataAnnotations;

namespace Web.Api.Orders.Contracts;

// The contract of the Orders page (track S1 of REACT_MIGRATION.md). Receipts has its own file next to this one.
//
//  * Money is a JSON number in the ORDER's currency (the currency the customer paid in); the notation is on the row, never a hard-coded "$".
//  * Timestamps are UTC with a Z; the client shows them in local time. `from` / `to` filters are LOCAL calendar days (yyyy-MM-dd).
//  * Order status is the number of the old enum: 0 Pending, 1 Processing, 2 Shipped, 3 Delivered, 4 Cancelled, 5 Returned.

/// <summary>One row of the orders list.</summary>
public sealed record OrderListItemDto
{
    public required int Id { get; init; }

    /// <summary>"ORD-000042". Built from the id, exactly as the old pages did.</summary>
    public required string OrderNumber { get; init; }

    /// <summary>Null for a walk-in sale (or a customer that was deleted since): the client shows "Walk-in customer".</summary>
    public int? CustomerId { get; init; }

    public string? CustomerName { get; init; }

    /// <summary>When the sale happened (UTC).</summary>
    public required DateTime OrderDate { get; init; }

    public required decimal TotalAmount { get; init; }

    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>0 Pending, 1 Processing, 2 Shipped, 3 Delivered, 4 Cancelled, 5 Returned.</summary>
    public required int Status { get; init; }

    /// <summary>Number of product lines on the order.</summary>
    public required int ItemCount { get; init; }
}

/// <summary>Revenue of one currency (orders are never summed across currencies).</summary>
public sealed record OrderRevenueDto
{
    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>The total of every order of the period that was not cancelled.</summary>
    public required decimal Amount { get; init; }
}

/// <summary>The tiles above the orders list. Always the last 30 days, as the old page.</summary>
public sealed record OrderSummaryDto
{
    public required int PeriodDays { get; init; }

    public required int TotalOrders { get; init; }

    public required int DeliveredOrders { get; init; }

    public required int PendingOrders { get; init; }

    public required int ProcessingOrders { get; init; }

    public required int CancelledOrders { get; init; }

    /// <summary>One entry per currency that sold something, biggest first. Empty when nothing was sold.</summary>
    public required IReadOnlyList<OrderRevenueDto> Revenue { get; init; }
}

/// <summary>One product line of an order.</summary>
public sealed record OrderLineDto
{
    public required int ProductId { get; init; }

    public required string ProductName { get; init; }

    /// <summary>Empty when the product has no barcode.</summary>
    public required string Barcode { get; init; }

    public required int Quantity { get; init; }

    /// <summary>The weighed / measured amount (in the product's base unit) for products sold by weight or volume; null for pieces.</summary>
    public decimal? WeightOrVolume { get; init; }

    public required decimal UnitPrice { get; init; }

    /// <summary>Unit price times the weighed amount, or times the quantity for pieces. Not rounded.</summary>
    public required decimal TotalPrice { get; init; }
}

/// <summary>A receipt issued for the order (normally one).</summary>
public sealed record OrderReceiptRefDto
{
    public required int Id { get; init; }

    public required string ReceiptNumber { get; init; }

    public required bool IsVoid { get; init; }
}

/// <summary>Everything the order page shows.</summary>
public sealed record OrderDetailDto
{
    public required int Id { get; init; }

    public required string OrderNumber { get; init; }

    public int? CustomerId { get; init; }

    public string? CustomerName { get; init; }

    public required DateTime OrderDate { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>Set once the order was edited (a status change, a cancellation).</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>0 Pending, 1 Processing, 2 Shipped, 3 Delivered, 4 Cancelled, 5 Returned.</summary>
    public required int Status { get; init; }

    public required int CurrencyId { get; init; }

    public required string CurrencyCode { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>Sum of the lines.</summary>
    public required decimal Subtotal { get; init; }

    /// <summary>The cart discount as money off, whether the cashier typed an amount or a percentage.</summary>
    public required decimal DiscountAmount { get; init; }

    /// <summary>The same discount as a percentage of <see cref="Subtotal"/>.</summary>
    public required decimal DiscountPercentage { get; init; }

    /// <summary>What the cashier added (positive) or took off (negative) the calculated total at checkout - cash rounding. 0 when nothing.</summary>
    public required decimal Adjustment { get; init; }

    /// <summary>Always 0 today: the till does not apply taxes (known unknown #4 of the migration plan).</summary>
    public required decimal TaxAmount { get; init; }

    public required decimal TotalAmount { get; init; }

    public required IReadOnlyList<OrderLineDto> Lines { get; init; }

    public required IReadOnlyList<OrderReceiptRefDto> Receipts { get; init; }
}

/// <summary>New status for an order.</summary>
public sealed class OrderStatusRequest
{
    /// <summary>0 Pending, 1 Processing, 2 Shipped, 3 Delivered, 4 Cancelled, 5 Returned. 4 reverses the whole sale.</summary>
    [Range(0, 5)]
    public int Status { get; set; }
}

/// <summary>What undoing a sale did. Shared by cancelling an order and voiding a receipt.</summary>
public sealed record SaleReversalDto
{
    /// <summary>False when the order had already been cancelled or returned: only the receipt was voided and stock and balances were left alone.</summary>
    public required bool OrderCancelled { get; init; }

    public required int ReceiptsVoided { get; init; }

    /// <summary>Order lines whose quantity went back into stock.</summary>
    public required int StockLinesRestored { get; init; }

    /// <summary>Amount taken off the customer's account (0 unless the sale was on debt).</summary>
    public required decimal DebtReversed { get; init; }
}

/// <summary>The order after a status change.</summary>
public sealed record OrderStatusResultDto
{
    public required int OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required int Status { get; init; }

    /// <summary>Set only when the new status was Cancelled: what the reversal did.</summary>
    public SaleReversalDto? Reversal { get; init; }
}
