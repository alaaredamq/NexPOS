using System.ComponentModel.DataAnnotations;

namespace Web.Api.Receipts.Contracts;

// The contract of the Receipts page (track S1 of REACT_MIGRATION.md). Same conventions as Orders: money in the receipt's own currency,
// UTC timestamps, `from` / `to` are LOCAL calendar days (yyyy-MM-dd).

/// <summary>One row of the receipts list.</summary>
public sealed record ReceiptListItemDto
{
    public required int Id { get; init; }

    public required string ReceiptNumber { get; init; }

    public required int OrderId { get; init; }

    public required DateTime ReceiptDate { get; init; }

    /// <summary>The name typed at the till, else the customer's name; null for a walk-in sale (the client shows "Walk-in customer").</summary>
    public string? CustomerName { get; init; }

    /// <summary>Empty when the cashier's account has no name.</summary>
    public required string CashierName { get; init; }

    public required string PaymentMethod { get; init; }

    public required decimal TotalAmount { get; init; }

    public required string CurrencyNotation { get; init; }

    public required bool IsVoid { get; init; }

    public DateTime? VoidDate { get; init; }

    public string? VoidReason { get; init; }

    /// <summary>Who voided it (falls back to the cashier, as the old list did, for receipts voided before the user was recorded).</summary>
    public string? VoidedBy { get; init; }
}

/// <summary>Sales of one currency in the stats period.</summary>
public sealed record ReceiptCurrencyStatsDto
{
    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>Total of the valid (not void) receipts.</summary>
    public required decimal TotalSales { get; init; }

    public required int ReceiptCount { get; init; }

    public required decimal AverageReceipt { get; init; }

    /// <summary>Sales per LOCAL day, oldest first; days without a sale are left out.</summary>
    public required IReadOnlyList<ReceiptDaySalesDto> Daily { get; init; }
}

public sealed record ReceiptDaySalesDto
{
    /// <summary>yyyy-MM-dd, the shop's local day.</summary>
    public required string Date { get; init; }

    public required decimal Amount { get; init; }
}

/// <summary>How many valid receipts were paid with a payment method.</summary>
public sealed record ReceiptPaymentShareDto
{
    public required string PaymentMethod { get; init; }

    public required int Count { get; init; }
}

/// <summary>The tiles and the analytics of the receipts page.</summary>
public sealed record ReceiptStatsDto
{
    /// <summary>The local days the numbers cover (yyyy-MM-dd). With no <c>from</c> / <c>to</c> in the request: the last 30 days up to today.</summary>
    public required string From { get; init; }

    public required string To { get; init; }

    public required int ValidCount { get; init; }

    public required int VoidCount { get; init; }

    /// <summary>One entry per currency that sold something, biggest first.</summary>
    public required IReadOnlyList<ReceiptCurrencyStatsDto> Currencies { get; init; }

    /// <summary>Valid receipts per payment method, most used first.</summary>
    public required IReadOnlyList<ReceiptPaymentShareDto> PaymentMethods { get; init; }
}

/// <summary>One product line of a receipt.</summary>
public sealed record ReceiptLineDto
{
    public required string ProductName { get; init; }

    public string? Barcode { get; init; }

    public required decimal Quantity { get; init; }

    /// <summary>Unit of measure ("kg", "piece" ...); null when none was recorded.</summary>
    public string? Unit { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal DiscountAmount { get; init; }

    public required decimal TaxAmount { get; init; }

    public required decimal LineTotal { get; init; }
}

/// <summary>One payment taken against a receipt (a sale can be split over several).</summary>
public sealed record ReceiptPaymentDto
{
    public required string PaymentMethod { get; init; }

    public string? ReferenceNumber { get; init; }

    public required decimal Amount { get; init; }

    public required DateTime PaymentDate { get; init; }
}

/// <summary>Everything the receipt page shows.</summary>
public sealed record ReceiptDetailDto
{
    public required int Id { get; init; }

    public required string ReceiptNumber { get; init; }

    public required int OrderId { get; init; }

    public required DateTime ReceiptDate { get; init; }

    public int? CustomerId { get; init; }

    /// <summary>Null for a walk-in sale.</summary>
    public string? CustomerName { get; init; }

    public string? CustomerPhone { get; init; }

    public required string CashierName { get; init; }

    public required string PaymentMethod { get; init; }

    public required string CurrencyCode { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>Rate to the base currency at the time of the sale; 1 when the sale was in the base currency.</summary>
    public required decimal ExchangeRate { get; init; }

    public required decimal SubTotal { get; init; }

    public required decimal DiscountAmount { get; init; }

    /// <summary>The discount as a percentage of <see cref="SubTotal"/> (derived: only the amount is stored).</summary>
    public required decimal DiscountPercentage { get; init; }

    public required decimal TaxAmount { get; init; }

    /// <summary>Cash rounding the cashier added (positive) or took off (negative); 0 when none.</summary>
    public required decimal Adjustment { get; init; }

    public required decimal TotalAmount { get; init; }

    public required decimal AmountPaid { get; init; }

    public required decimal ChangeAmount { get; init; }

    /// <summary>True when the payments cover the total.</summary>
    public required bool IsFullyPaid { get; init; }

    public string? Notes { get; init; }

    public required bool IsVoid { get; init; }

    public DateTime? VoidDate { get; init; }

    public string? VoidReason { get; init; }

    public string? VoidedBy { get; init; }

    public required DateTime CreatedAt { get; init; }

    public string? PrintedBy { get; init; }

    public DateTime? PrintedAt { get; init; }

    public required IReadOnlyList<ReceiptLineDto> Lines { get; init; }

    public required IReadOnlyList<ReceiptPaymentDto> Payments { get; init; }
}

/// <summary>Why a receipt is being voided.</summary>
public sealed class ReceiptVoidRequest
{
    /// <summary>Required, as the old void dialogs demanded it. It goes on the receipt and into the audit trail.</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Answer of a reprint. A printer problem is not an HTTP error: <c>printed</c> is false and the message says what to do.</summary>
public sealed record ReceiptPrintResultDto
{
    public required bool Printed { get; init; }

    /// <summary>Already localized.</summary>
    public required string Message { get; init; }

    /// <summary>Where to download the same receipt as a PDF when the printer could not be reached.</summary>
    public required string PdfUrl { get; init; }
}
