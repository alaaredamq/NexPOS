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