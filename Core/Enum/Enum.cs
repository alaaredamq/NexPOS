namespace Core.Enum;

public enum OrderStatus
{
    Pending = 0,
    Processing = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4,
    Returned = 5
}

public enum PaymentStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Refunded = 3
}

public enum TaxType
{
    VAT = 0,
    SalesTax = 1,
    ServiceTax = 2,
    ExciseTax = 3,
    CustomTax = 4
}

/// <summary>
/// Labels a <c>CustomerDebtTransaction</c> entry for display/reporting. The transaction's
/// signed Amount (not this Type) is what actually drives the customer's balance.
/// </summary>
public enum CustomerDebtTransactionType
{
    /// <summary>A POS sale completed on the customer's account (positive amount).</summary>
    Sale = 0,
    /// <summary>A payment settling part of the balance (negative amount).</summary>
    Payment = 1,
    /// <summary>A manual correction entered by staff (either sign).</summary>
    ManualAdjustment = 2,
    /// <summary>A sale that was voided/cancelled: cancels out its Sale entry (negative amount).</summary>
    SaleReversal = 3
}

/// <summary>
/// Labels a <c>SupplierDebtTransaction</c> entry for display/reporting. The transaction's
/// signed Amount (not this Type) is what actually drives the supplier's balance.
/// </summary>
public enum SupplierDebtTransactionType
{
    /// <summary>Goods or services taken from the supplier on credit (positive amount).</summary>
    Purchase = 0,
    /// <summary>A payment made to the supplier settling part of the balance (negative amount).</summary>
    Payment = 1,
    /// <summary>A manual correction entered by staff (either sign).</summary>
    ManualAdjustment = 2
}