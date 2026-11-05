namespace Core.IRepositories.Sales;

// Light, read-only shapes for the orders and receipts lists. The list pages need a handful of columns for every row in a
// date range, not the whole order graph (cart items, products, ...) that IOrderRepository.GetByDateRangeAsync loads.

/// <summary>Which orders a list asks for. Free-text search, sorting and paging are the caller's: the repository only narrows by indexed columns.</summary>
/// <param name="FromUtc">Inclusive lower bound of <c>OrderDate</c> (UTC), or null.</param>
/// <param name="ToUtc">Inclusive upper bound of <c>OrderDate</c> (UTC), or null.</param>
/// <param name="Status">An <c>OrderStatus</c> value, or null for every status.</param>
/// <param name="CustomerId">Only this customer's orders, or null.</param>
public sealed record OrderListFilter(DateTime? FromUtc = null, DateTime? ToUtc = null, int? Status = null, int? CustomerId = null);

/// <summary>One order as the list shows it.</summary>
public sealed record OrderListRow(
    int Id,
    int? CustomerId,
    string? CustomerName,
    DateTime OrderDate,
    decimal TotalAmount,
    int Status,
    int CurrencyId,
    string CurrencyNotation,
    int ItemCount);

/// <summary>Revenue and gross profit over every completed (not cancelled or returned) order ever made.</summary>
public sealed record LifetimeSalesTotals(decimal Revenue, decimal Profit);

/// <summary>Which receipts a list asks for.</summary>
/// <param name="FromUtc">Inclusive lower bound of <c>ReceiptDate</c> (UTC), or null.</param>
/// <param name="ToUtc">Inclusive upper bound of <c>ReceiptDate</c> (UTC), or null.</param>
/// <param name="IsVoid">True = only void receipts, false = only valid ones, null = both.</param>
public sealed record ReceiptListFilter(DateTime? FromUtc = null, DateTime? ToUtc = null, bool? IsVoid = null);

/// <summary>One receipt as the list shows it. <c>CustomerName</c> is what was typed at the till, else the customer's name; null for a walk-in.</summary>
public sealed record ReceiptListRow(
    int Id,
    string ReceiptNumber,
    int OrderId,
    DateTime ReceiptDate,
    string? CustomerName,
    string CashierName,
    string PaymentMethod,
    decimal TotalAmount,
    int CurrencyId,
    string CurrencyNotation,
    bool IsVoid,
    DateTime? VoidDate,
    string? VoidReason,
    string? VoidedBy,
    string? CustomerPhone);
