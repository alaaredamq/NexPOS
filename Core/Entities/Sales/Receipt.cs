using Common.Helpers;
using Core.Entities.Base;
using Core.Entities.Users;
using Core.Entities.Products;
using Core.Enum;

namespace Core.Entities.Sales;

public class Receipt : ISSoftDeletable, IEntityBase<int>
{
    public string ReceiptNumber { get; set; } = null!;
    public int OrderId { get; set; }
    /// <summary>
    /// Customer ID. Null for walk-in/anonymous customers.
    /// </summary>
    public int? CustomerId { get; set; }
    public int CashierId { get; set; }
    public DateTime ReceiptDate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal ChangeAmount { get; set; }
    public int PaymentMethodId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1.0m;
    public string? Notes { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public bool IsVoid { get; set; } = false;
    public DateTime? VoidDate { get; set; }
    public int? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
    public string? PrintedBy { get; set; }
    public DateTime? PrintedAt { get; set; }
    public string? EmailSentTo { get; set; }
    public DateTime? EmailSentAt { get; set; }

    #region Navigation Properties

    public Order Order { get; set; } = null!;
    public Customer? Customer { get; set; }
    public User Cashier { get; set; } = null!;
    public User? VoidedByUser { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public ICollection<ReceiptItem> ReceiptItems { get; set; } = new List<ReceiptItem>();
    public ICollection<ReceiptPayment> ReceiptPayments { get; set; } = new List<ReceiptPayment>();

    #endregion

    #region Constructors

    public Receipt(string receiptNumber, int orderId, int? customerId, int cashierId, 
        decimal subTotal, decimal taxAmount, decimal discountAmount, decimal totalAmount,
        int paymentMethodId, int currencyId, decimal exchangeRate = 1.0m)
    {
        ReceiptNumber = receiptNumber;
        OrderId = orderId;
        CustomerId = customerId;
        CashierId = cashierId;
        ReceiptDate = DateTime.UtcNow;
        SubTotal = subTotal;
        TaxAmount = taxAmount;
        DiscountAmount = discountAmount;
        TotalAmount = totalAmount;
        AmountPaid = totalAmount; // Default to full payment
        ChangeAmount = 0;
        PaymentMethodId = paymentMethodId;
        CurrencyId = currencyId;
        ExchangeRate = exchangeRate;
        CreatedAt = DateTime.UtcNow;
    }

    protected Receipt() { }

    #endregion

    #region Methods

    public void VoidReceipt(int voidedByUserId, string reason)
    {
        IsVoid = true;
        VoidDate = DateTime.UtcNow;
        VoidedByUserId = voidedByUserId;
        VoidReason = reason;
    }

    public void MarkAsPrinted(string printedBy)
    {
        PrintedBy = printedBy;
        PrintedAt = DateTime.UtcNow;
    }

    public void MarkEmailSent(string email)
    {
        EmailSentTo = email;
        EmailSentAt = DateTime.UtcNow;
    }

    public decimal CalculateChangeAmount()
    {
        ChangeAmount = AmountPaid - TotalAmount;
        return ChangeAmount;
    }

    public bool IsFullyPaid()
    {
        return AmountPaid >= TotalAmount;
    }

    public string GetFormattedTotal()
    {
        var notation = Currency?.Notation ?? "$";
        return $"{notation}{TotalAmount:N2}";
    }

    #endregion
}

public class ReceiptItem : ISSoftDeletable, IEntityBase<int>
{
    public int ReceiptId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string? ProductBarcode { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    
    /// <summary>
    /// Weight or volume amount for products sold by weight/volume (e.g., 0.5 for 500g or 0.5L).
    /// Null for products sold by piece. The unit is determined by the Unit field.
    /// </summary>
    public decimal? WeightOrVolume { get; set; }
    
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }

    #region Navigation Properties

    public Receipt Receipt { get; set; } = null!;
    public Product Product { get; set; } = null!;

    #endregion

    #region Constructors

    public ReceiptItem(int receiptId, int productId, string productName, decimal quantity, 
        decimal unitPrice, decimal taxRate, string? productBarcode = null, string? unit = null, decimal? weightOrVolume = null)
    {
        ReceiptId = receiptId;
        ProductId = productId;
        ProductName = productName;
        ProductBarcode = productBarcode;
        Quantity = quantity;
        Unit = unit;
        WeightOrVolume = weightOrVolume;
        UnitPrice = unitPrice;
        TaxRate = taxRate;
        DiscountAmount = 0;
        TaxAmount = (unitPrice * quantity) * taxRate;
        LineTotal = (unitPrice * quantity) + TaxAmount - DiscountAmount;
        CreatedAt = DateTime.UtcNow;
    }

    protected ReceiptItem() { }

    #endregion

    #region Methods

    public void ApplyDiscount(decimal discountAmount)
    {
        DiscountAmount = discountAmount;
        RecalculateLineTotal();
    }

    public void RecalculateLineTotal()
    {
        var subtotal = UnitPrice * Quantity;
        TaxAmount = subtotal * TaxRate;
        LineTotal = subtotal + TaxAmount - DiscountAmount;
    }

    #endregion
}

public class ReceiptPayment : ISSoftDeletable, IEntityBase<int>
{
    public int ReceiptId { get; set; }
    public int PaymentMethodId { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime PaymentDate { get; set; }

    #region Navigation Properties

    public Receipt Receipt { get; set; } = null!;
    public PaymentMethod PaymentMethod { get; set; } = null!;

    #endregion

    #region Constructors

    public ReceiptPayment(int receiptId, int paymentMethodId, decimal amount, 
        string? referenceNumber = null, string? notes = null)
    {
        ReceiptId = receiptId;
        PaymentMethodId = paymentMethodId;
        Amount = amount;
        ReferenceNumber = referenceNumber;
        Notes = notes;
        PaymentDate = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }

    protected ReceiptPayment() { }

    #endregion
}
