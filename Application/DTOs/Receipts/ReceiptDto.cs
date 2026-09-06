namespace Application.DTOs.Receipts;

public class ReceiptDto
{
    public int Id { get; set; }
    public string ReceiptNumber { get; set; } = null!;
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public int CashierId { get; set; }
    public string? CashierName { get; set; }
    public DateTime ReceiptDate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal ChangeAmount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Currency { get; set; }
    public string? CurrencyNotation { get; set; }
    public decimal ExchangeRate { get; set; }
    public string? Notes { get; set; }
    public bool IsVoid { get; set; }
    public DateTime? VoidDate { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ReceiptItemDto> Items { get; set; } = new();
    public List<ReceiptPaymentDto> Payments { get; set; } = new();
}

public class ReceiptItemDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string? ProductBarcode { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    
    /// <summary>
    /// Weight or volume amount for products sold by weight/volume (e.g., 0.5 for 500g or 0.5L).
    /// Null for products sold by piece.
    /// </summary>
    public decimal? WeightOrVolume { get; set; }
    
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
}

public class ReceiptPaymentDto
{
    public int Id { get; set; }
    public string PaymentMethod { get; set; } = null!;
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime PaymentDate { get; set; }
}

public class ReceiptListDto
{
    public int Id { get; set; }
    public string ReceiptNumber { get; set; } = null!;
    public DateTime ReceiptDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CashierName { get; set; }
    public decimal TotalAmount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Currency { get; set; }
    public bool IsVoid { get; set; }
}

public class CreateReceiptDto
{
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public int CashierId { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public int PaymentMethodId { get; set; }
    public int CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1.0m;
    public string? Notes { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public List<CreateReceiptItemDto> Items { get; set; } = new();
    public List<CreateReceiptPaymentDto> Payments { get; set; } = new();
}

public class CreateReceiptItemDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string? ProductBarcode { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    
    /// <summary>
    /// Weight or volume amount for products sold by weight/volume (e.g., 0.5 for 500g or 0.5L).
    /// Null for products sold by piece.
    /// </summary>
    public decimal? WeightOrVolume { get; set; }
    
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public string? Notes { get; set; }
}

public class CreateReceiptPaymentDto
{
    public int PaymentMethodId { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public class VoidReceiptDto
{
    public int ReceiptId { get; set; }
    public int UserId { get; set; }
    public string Reason { get; set; } = null!;
}

public class ReceiptSearchDto
{
    public string? SearchTerm { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? CustomerId { get; set; }
    public int? CashierId { get; set; }
    public bool? IsVoid { get; set; }
}

