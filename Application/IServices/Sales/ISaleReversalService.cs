using Application.IServices.Base;

namespace Application.IServices.Sales;

/// <summary>What a void / cancel actually did, so the UI and the audit trail can say it.</summary>
/// <param name="OrderCancelled">False when the order had already been cancelled/returned (nothing left to reverse).</param>
/// <param name="ReceiptsVoided">Receipts of the order that were marked void by this call.</param>
/// <param name="StockLinesRestored">Order lines whose quantity went back into stock.</param>
/// <param name="DebtReversed">Amount taken off the customer's account (0 unless the sale was placed on debt).</param>
public sealed record SaleReversalResult(
    int OrderId,
    string OrderNumber,
    bool OrderCancelled,
    int ReceiptsVoided,
    int StockLinesRestored,
    decimal DebtReversed);

/// <summary>
/// Undoes a completed sale. Voiding a receipt and cancelling an order are the same business event
/// seen from two screens, so both come here and get the same all-or-nothing result: the receipt(s)
/// are voided, the order is cancelled, the stock goes back on the shelf, a debt sale comes off the
/// customer's account, and the open cash drawer stops expecting that money.
/// </summary>
public interface ISaleReversalService : IBaseService
{
    /// <summary>
    /// Voids the receipt and reverses the sale behind it. Throws when the receipt does not exist or
    /// is already void.
    /// </summary>
    Task<SaleReversalResult> VoidReceiptAsync(int receiptId, int userId, string? reason);

    /// <summary>
    /// Cancels the order, voids its receipt(s) and reverses the sale. Throws when the order does not
    /// exist or has already been cancelled/returned.
    /// </summary>
    Task<SaleReversalResult> CancelOrderAsync(int orderId, int userId, string? reason);
}
