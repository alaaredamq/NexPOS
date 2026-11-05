using Application.IServices.Base;

namespace Application.IServices.Sales;

/// <summary>What changing a sale's customer did.</summary>
/// <param name="Changed">False when the sale already belonged to that customer (nothing was touched).</param>
/// <param name="ReceiptsUpdated">Receipts of the order that now name the new customer.</param>
/// <param name="DebtMoved">Amount moved from the old customer's account to the new one (0 unless the sale was placed on debt).</param>
public sealed record SaleCustomerResult(
    int OrderId,
    string OrderNumber,
    int? CustomerId,
    bool Changed,
    int ReceiptsUpdated,
    decimal DebtMoved);

public enum SaleCustomerFailure
{
    OrderNotFound,
    CustomerNotFound,
    /// <summary>The order was cancelled or returned: it is out of the books, so its customer is history.</summary>
    OrderClosed,
    /// <summary>A sale on the customer's account cannot be left without a customer.</summary>
    DebtNeedsCustomer,
}

/// <summary>A change of customer was refused; <see cref="Failure"/> says why.</summary>
public sealed class SaleCustomerException : Exception
{
    public SaleCustomerFailure Failure { get; }

    public SaleCustomerException(SaleCustomerFailure failure, string message) : base(message) => Failure = failure;
}

/// <summary>
/// Fixes the customer of a completed sale (picked wrongly, or the customer asked for the receipt in their name afterwards).
/// A receipt and its order are one sale seen from two screens, so both come here: the order, its cart and its receipts all
/// move together, and a sale put on the customer's account moves its debt with it - all or nothing.
/// </summary>
public interface ISaleCustomerService : IBaseService
{
    /// <summary>
    /// Gives the order (and its receipts) to <paramref name="customerId"/>; null = walk-in. Throws
    /// <see cref="SaleCustomerException"/> when the order or customer does not exist, the sale was cancelled/returned,
    /// or it was placed on debt and no customer is given.
    /// </summary>
    Task<SaleCustomerResult> ChangeCustomerAsync(int orderId, int? customerId, int userId);
}
