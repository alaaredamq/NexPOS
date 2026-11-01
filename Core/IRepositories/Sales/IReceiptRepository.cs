using Core.Entities.Sales;
using Core.IRepositories.Base;

namespace Core.IRepositories.Sales;

public interface IReceiptRepository : IRepository<Receipt>
{
    Task<Receipt?> GetByReceiptNumberAsync(string receiptNumber);
    Task<List<Receipt>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<List<Receipt>> GetByCashierIdAsync(int cashierId, DateTime? startDate = null, DateTime? endDate = null);
    Task<List<Receipt>> GetByCustomerIdAsync(int customerId);
    Task<List<Receipt>> GetByOrderIdAsync(int orderId);
    Task<List<Receipt>> GetVoidReceiptsAsync(DateTime? startDate = null, DateTime? endDate = null);
    Task<decimal> GetTotalSalesByDateAsync(DateTime date);
    Task<decimal> GetTotalSalesByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<int> GetReceiptCountByDateAsync(DateTime date);
    Task<int> GetReceiptCountByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<List<Receipt>> GetRecentReceiptsAsync(int count = 10);
    Task<string> GenerateNextReceiptNumberAsync();
    Task<List<Receipt>> SearchReceiptsAsync(string searchTerm);

    /// <summary>The receipts matching the filter as light list rows (no items or payments loaded), newest first. Deleted receipts are left out.</summary>
    Task<List<ReceiptListRow>> GetListRowsAsync(ReceiptListFilter filter);
}
