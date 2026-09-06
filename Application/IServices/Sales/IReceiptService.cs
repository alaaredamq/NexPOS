using Application.IServices.Base;
using Core.Entities.Sales;

namespace Application.IServices.Sales;

public interface IReceiptService : IBaseService
{
    Task<List<Receipt>> GetAllAsync();
    Task<Receipt> CreateReceiptAsync(CreateReceiptRequest request);
    Task<Receipt> GetReceiptAsync(int id);
    Task<Receipt> GetReceiptByNumberAsync(string receiptNumber);
    Task<List<Receipt>> GetReceiptsByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<List<Receipt>> GetReceiptsByCashierAsync(int cashierId, DateTime? startDate = null, DateTime? endDate = null);
    Task<List<Receipt>> GetReceiptsByCustomerAsync(int customerId);
    Task<List<Receipt>> GetRecentReceiptsAsync(int count = 10);
    Task<List<Receipt>> SearchReceiptsAsync(string searchTerm);
    Task<Receipt> VoidReceiptAsync(int receiptId, int userId, string reason);
    Task<string> GenerateReceiptNumberAsync();
    Task<byte[]> GenerateReceiptPdfAsync(int receiptId);
    Task<byte[]> GenerateReceiptPdfAsync(string receiptNumber);
    Task SendReceiptEmailAsync(int receiptId, string emailAddress);
    Task<ReceiptStatsDTO> GetReceiptStatsAsync(DateTime? startDate = null, DateTime? endDate = null);
    Task<List<Receipt>> GetVoidReceiptsAsync(DateTime? startDate = null, DateTime? endDate = null);
    Task<decimal> GetTotalSalesByDateAsync(DateTime date);
    Task<decimal> GetTotalSalesByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<int> GetReceiptCountByDateAsync(DateTime date);
    Task<int> GetReceiptCountByDateRangeAsync(DateTime startDate, DateTime endDate);
}

public class CreateReceiptRequest
{
    public int OrderId { get; set; }
    /// <summary>
    /// Customer ID. Null or 0 for walk-in/anonymous customers.
    /// </summary>
    public int? CustomerId { get; set; }
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
    public List<ReceiptItemRequest> Items { get; set; } = new();
    public List<ReceiptPaymentRequest> Payments { get; set; } = new();
}

public class ReceiptItemRequest
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string? ProductBarcode { get; set; }
    public decimal Quantity { get; set; } // Changed to decimal to support weight/volume products
    public string? Unit { get; set; } // Unit of measurement (kg, g, liter, ml, piece, etc.)
    
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

public class ReceiptPaymentRequest
{
    public int PaymentMethodId { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public class ReceiptStatsDTO
{
    public decimal TotalSales { get; set; }
    public int TotalReceipts { get; set; }
    public int VoidReceipts { get; set; }
    public decimal AverageReceiptValue { get; set; }
    public decimal TotalTaxCollected { get; set; }
    public decimal TotalDiscountsGiven { get; set; }
    public Dictionary<string, int> PaymentMethodBreakdown { get; set; } = new();
    public Dictionary<string, decimal> DailySales { get; set; } = new();
}

