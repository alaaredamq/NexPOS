using System.Drawing.Printing;
using Application.IServices.Sales;
using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales;

public class ReceiptService : IReceiptService
{
    private readonly IReceiptRepository _receiptRepository;
    private readonly ILogger<ReceiptService> _logger;

    public ReceiptService(IReceiptRepository receiptRepository, ILogger<ReceiptService> logger)
    {
        _receiptRepository = receiptRepository;
        _logger = logger;
    }

    public async Task<Receipt> CreateReceiptAsync(CreateReceiptRequest request)
    {
        try
        {
            var receiptNumber = await _receiptRepository.GenerateNextReceiptNumberAsync();
            
            var receipt = new Receipt(
                receiptNumber,
                request.OrderId,
                request.CustomerId,
                request.CashierId,
                request.SubTotal,
                request.TaxAmount,
                request.DiscountAmount,
                request.TotalAmount,
                request.PaymentMethodId,
                request.CurrencyId,
                request.ExchangeRate
            )
            {
                AmountPaid = request.AmountPaid,
                Notes = request.Notes,
                CustomerName = request.CustomerName,
                CustomerPhone = request.CustomerPhone
            };

            // Add receipt items
            foreach (var itemRequest in request.Items)
            {
                var receiptItem = new ReceiptItem(
                    receipt.Id, // This will be set after receipt is saved
                    itemRequest.ProductId,
                    itemRequest.ProductName,
                    itemRequest.Quantity,
                    itemRequest.UnitPrice,
                    itemRequest.TaxRate,
                    itemRequest.ProductBarcode,
                    itemRequest.Unit,
                    itemRequest.WeightOrVolume
                );
                
                if (itemRequest.DiscountAmount > 0)
                {
                    receiptItem.ApplyDiscount(itemRequest.DiscountAmount);
                }
                
                receiptItem.Notes = itemRequest.Notes;
                receipt.ReceiptItems.Add(receiptItem);
            }

            // Add receipt payments
            foreach (var paymentRequest in request.Payments)
            {
                var receiptPayment = new ReceiptPayment(
                    receipt.Id, // This will be set after receipt is saved
                    paymentRequest.PaymentMethodId,
                    paymentRequest.Amount,
                    paymentRequest.ReferenceNumber,
                    paymentRequest.Notes
                );
                receipt.ReceiptPayments.Add(receiptPayment);
            }

            receipt.CalculateChangeAmount();
            
                return await _receiptRepository.SaveAsync(receipt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating receipt for order {OrderId}", request.OrderId);
            throw;
        }
    }

    public async Task<Receipt> GetReceiptAsync(int id)
    {
        return await _receiptRepository.GetByIdAsync(id);
    }

    public async Task<Receipt> GetReceiptByNumberAsync(string receiptNumber)
    {
        var receipt = await _receiptRepository.GetByReceiptNumberAsync(receiptNumber);
        if (receipt == null)
            throw new InvalidOperationException($"Receipt with number {receiptNumber} not found.");
        return receipt;
    }

    public async Task<List<Receipt>> GetReceiptsByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _receiptRepository.GetByDateRangeAsync(startDate, endDate);
    }

    public async Task<List<Receipt>> GetReceiptsByCashierAsync(int cashierId, DateTime? startDate = null, DateTime? endDate = null)
    {
        return await _receiptRepository.GetByCashierIdAsync(cashierId, startDate, endDate);
    }

    public async Task<List<Receipt>> GetReceiptsByCustomerAsync(int customerId)
    {
        return await _receiptRepository.GetByCustomerIdAsync(customerId);
    }

    public async Task<List<Receipt>> GetRecentReceiptsAsync(int count = 10)
    {
        return await _receiptRepository.GetRecentReceiptsAsync(count);
    }

    public async Task<List<Receipt>> SearchReceiptsAsync(string searchTerm)
    {
        return await _receiptRepository.SearchReceiptsAsync(searchTerm);
    }

    public async Task<Receipt> VoidReceiptAsync(int receiptId, int userId, string reason)
    {
        var receipt = await _receiptRepository.GetByIdAsync(receiptId);
        receipt.VoidReceipt(userId, reason);
        return await _receiptRepository.SaveAsync(receipt);
    }

    public async Task<string> GenerateReceiptNumberAsync()
    {
        return await _receiptRepository.GenerateNextReceiptNumberAsync();
    }

    public async Task<byte[]> GenerateReceiptPdfAsync(int receiptId)
    {
        var receipt = await _receiptRepository.GetByIdAsync(receiptId);
        return await GenerateReceiptPdfAsync(receipt.ReceiptNumber);
    }

    public async Task<byte[]> GenerateReceiptPdfAsync(string receiptNumber)
    {
        var receipt = await _receiptRepository.GetByReceiptNumberAsync(receiptNumber);
        if (receipt == null)
            throw new InvalidOperationException($"Receipt with number {receiptNumber} not found.");

        // Generate HTML receipt
        var html = GenerateReceiptHtml(receipt);
        
        // For now, return HTML as bytes
        // In production, you would use a library like:
        // - QuestPDF
        // - iTextSharp/iText7
        // - DinkToPdf
        // - SelectPdf
        // to convert HTML to PDF
        
        return System.Text.Encoding.UTF8.GetBytes(html);
    }

    private string GenerateReceiptHtml(Receipt receipt)
    {
        var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Receipt - {receipt.ReceiptNumber}</title>
    <style>
        body {{ font-family: 'Courier New', monospace; max-width: 800px; margin: 0 auto; padding: 20px; }}
        .header {{ text-align: center; border-bottom: 2px solid #000; padding-bottom: 10px; margin-bottom: 20px; }}
        .company-name {{ font-size: 24px; font-weight: bold; }}
        .receipt-info {{ margin: 20px 0; }}
        .receipt-info table {{ width: 100%; }}
        .receipt-info td {{ padding: 5px; }}
        .items {{ margin: 20px 0; }}
        .items table {{ width: 100%; border-collapse: collapse; }}
        .items th, .items td {{ border-bottom: 1px solid #ddd; padding: 10px; text-align: left; }}
        .items th {{ background-color: #f5f5f5; font-weight: bold; }}
        .totals {{ margin: 20px 0; float: right; }}
        .totals table {{ width: 300px; }}
        .totals td {{ padding: 5px; }}
        .total-row {{ font-weight: bold; font-size: 18px; border-top: 2px solid #000; }}
        .footer {{ text-align: center; margin-top: 40px; border-top: 2px solid #000; padding-top: 10px; }}
    </style>
</head>
<body>
    <div class='header'>
        <div class='company-name'>YOUR COMPANY NAME</div>
        <div>123 Main Street, City, State 12345</div>
        <div>Phone: (123) 456-7890 | Email: info@company.com</div>
        <div>Tax ID: 123-456-7890</div>
    </div>

    <div class='receipt-info'>
        <table>
            <tr>
                <td width='50%'><strong>Receipt Number:</strong> {receipt.ReceiptNumber}</td>
                <td><strong>Date:</strong> {receipt.ReceiptDate:yyyy-MM-dd HH:mm}</td>
            </tr>
            <tr>
                <td><strong>Customer:</strong> {receipt.CustomerName ?? (receipt.Customer != null ? $"{receipt.Customer.FirstName} {receipt.Customer.LastName}" : "Walk-in")}</td>
                <td><strong>Cashier:</strong> {receipt.Cashier?.FirstName} {receipt.Cashier?.LastName}</td>
            </tr>
            {(!string.IsNullOrEmpty(receipt.CustomerPhone) ? $"<tr><td colspan='2'><strong>Phone:</strong> {receipt.CustomerPhone}</td></tr>" : "")}
        </table>
    </div>

    <div class='items'>
        <table>
            <thead>
                <tr>
                    <th>Item</th>
                    <th style='text-align: center;'>Qty</th>
                    <th style='text-align: right;'>Price</th>
                    <th style='text-align: right;'>Discount</th>
                    <th style='text-align: right;'>Tax</th>
                    <th style='text-align: right;'>Total</th>
                </tr>
            </thead>
            <tbody>
                {string.Join("", receipt.ReceiptItems.Where(i => !i.IsDeleted).Select(item => $@"
                <tr>
                    <td>
                        <strong>{item.ProductName}</strong>
                        {(!string.IsNullOrEmpty(item.ProductBarcode) ? $"<br><small>SKU: {item.ProductBarcode}</small>" : "")}
                    </td>
                    <td style='text-align: center;'>
                        {item.Quantity:N3}{(string.IsNullOrEmpty(item.Unit) || item.Unit == "piece" ? "" : $" {item.Unit}")}
                    </td>
                    <td style='text-align: right;'>{receipt.Currency.Notation}{item.UnitPrice:N2}</td>
                    <td style='text-align: right;'>{(item.DiscountAmount > 0 ? $"-{receipt.Currency.Notation}{item.DiscountAmount:N2}" : "-")}</td>
                    <td style='text-align: right;'>{receipt.Currency.Notation}{item.TaxAmount:N2}</td>
                    <td style='text-align: right;'><strong>{receipt.Currency.Notation}{item.LineTotal:N2}</strong></td>
                </tr>
                "))}
            </tbody>
        </table>
    </div>

    <div style='clear: both;'></div>
    
    <div class='totals'>
        <table>
            <tr>
                <td>Subtotal:</td>
                <td style='text-align: right;'>{receipt.Currency.Notation}{receipt.SubTotal:N2}</td>
            </tr>
            {(receipt.DiscountAmount > 0 ? $@"
            <tr>
                <td>Discount:</td>
                <td style='text-align: right; color: red;'>-{receipt.Currency.Notation}{receipt.DiscountAmount:N2}</td>
            </tr>" : "")}
            <tr>
                <td>Tax:</td>
                <td style='text-align: right;'>{receipt.Currency.Notation}{receipt.TaxAmount:N2}</td>
            </tr>
            <tr class='total-row'>
                <td>TOTAL:</td>
                <td style='text-align: right;'>{receipt.Currency.Notation}{receipt.TotalAmount:N2}</td>
            </tr>
            <tr>
                <td>Amount Paid:</td>
                <td style='text-align: right;'>{receipt.Currency.Notation}{receipt.AmountPaid:N2}</td>
            </tr>
            {(receipt.ChangeAmount > 0 ? $@"
            <tr>
                <td>Change:</td>
                <td style='text-align: right;'>{receipt.Currency.Notation}{receipt.ChangeAmount:N2}</td>
            </tr>" : "")}
        </table>
    </div>

    <div style='clear: both;'></div>

    <div class='footer'>
        <p><strong>THANK YOU FOR YOUR PURCHASE!</strong></p>
        <p>Payment Method: {receipt.PaymentMethod?.Name}</p>
        {(receipt.IsVoid ? $"<p style='color: red; font-weight: bold;'>*** VOID ***</p><p>Reason: {receipt.VoidReason}</p>" : "")}
        <p style='font-size: 12px;'>Please keep this receipt for your records</p>
    </div>
</body>
</html>";

        return html;
    }

    public async Task SendReceiptEmailAsync(int receiptId, string emailAddress)
    {
        var receipt = await _receiptRepository.GetByIdAsync(receiptId);
        
        // TODO: Implement email sending functionality
        // For now, just mark as sent
        receipt.MarkEmailSent(emailAddress);
        await _receiptRepository.SaveAsync(receipt);
    }

    public async Task<ReceiptStatsDTO> GetReceiptStatsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        // Use UTC dates to match ReceiptDate which may be stored in UTC
        var utcNow = DateTime.UtcNow;
        var start = startDate ?? utcNow.Date.AddDays(-30);
        var end = endDate ?? utcNow.Date.AddDays(1).AddTicks(-1); // End of today

        var receipts = await _receiptRepository.GetByDateRangeAsync(start, end);
        var validReceipts = receipts.Where(r => !r.IsVoid).ToList();

        var stats = new ReceiptStatsDTO
        {
            TotalSales = validReceipts.Sum(r => r.TotalAmount),
            TotalReceipts = validReceipts.Count,
            VoidReceipts = receipts.Count(r => r.IsVoid),
            AverageReceiptValue = validReceipts.Any() ? validReceipts.Average(r => r.TotalAmount) : 0,
            TotalTaxCollected = validReceipts.Sum(r => r.TaxAmount),
            TotalDiscountsGiven = validReceipts.Sum(r => r.DiscountAmount)
        };

        // Payment method breakdown (with null check)
        stats.PaymentMethodBreakdown = validReceipts
            .Where(r => r.PaymentMethod != null)
            .GroupBy(r => r.PaymentMethod.Name ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        // Daily sales breakdown
        stats.DailySales = validReceipts
            .GroupBy(r => r.ReceiptDate.Date)
            .ToDictionary(g => g.Key.ToString("yyyy-MM-dd"), g => g.Sum(r => r.TotalAmount));

        return stats;
    }

    public async Task<List<Receipt>> GetVoidReceiptsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        return await _receiptRepository.GetVoidReceiptsAsync(startDate, endDate);
    }

    public async Task<decimal> GetTotalSalesByDateAsync(DateTime date)
    {
        return await _receiptRepository.GetTotalSalesByDateAsync(date);
    }

    public async Task<decimal> GetTotalSalesByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _receiptRepository.GetTotalSalesByDateRangeAsync(startDate, endDate);
    }

    public async Task<int> GetReceiptCountByDateAsync(DateTime date)
    {
        return await _receiptRepository.GetReceiptCountByDateAsync(date);
    }

    public async Task<int> GetReceiptCountByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _receiptRepository.GetReceiptCountByDateRangeAsync(startDate, endDate);
    }

    // IBaseService implementation
    public async Task<List<Receipt>> GetAllAsync()
    {
        return await _receiptRepository.GetAllAsync();
    }

    public async Task<Receipt> GetByIdAsync(int id)
    {
        return await _receiptRepository.GetByIdAsync(id);
    }

    public async Task<Receipt> CreateAsync(Receipt entity)
    {
        return await _receiptRepository.SaveAsync(entity);
    }

    public async Task<Receipt> UpdateAsync(int id, Receipt entity)
    {
        return await _receiptRepository.SaveAsync(entity);
    }

    public async Task DeleteAsync(int id)
    {
        await _receiptRepository.DeleteAsync(id);
    }
}
