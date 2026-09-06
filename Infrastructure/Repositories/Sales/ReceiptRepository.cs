using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Sales;

public class ReceiptRepository : Repository<Receipt>, IReceiptRepository
{
    public ReceiptRepository(ErpDbContext context, IServiceProvider serviceProvider) : base(context, serviceProvider)
    {
    }

    public new async Task<Receipt> GetByIdAsync(int id)
    {
        var receipt = await GetAllQueryable()
            .Include(r => r.Order)
            .Include(r => r.Customer)
            .Include(r => r.Cashier)
            .Include(r => r.VoidedByUser)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Currency)
            .Include(r => r.ReceiptItems)
                .ThenInclude(ri => ri.Product)
            .Include(r => r.ReceiptPayments)
                .ThenInclude(rp => rp.PaymentMethod)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (receipt == null)
            throw new ArgumentNullException($"Receipt with id {id} not found");

        return receipt;
    }

    public async Task<Receipt?> GetByReceiptNumberAsync(string receiptNumber)
    {
        return await _Context.Set<Receipt>()
            .Include(r => r.Order)
            .Include(r => r.Customer)
            .Include(r => r.Cashier)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Currency)
            .Include(r => r.ReceiptItems)
                .ThenInclude(ri => ri.Product)
            .Include(r => r.ReceiptPayments)
            .FirstOrDefaultAsync(r => r.ReceiptNumber == receiptNumber && !r.IsDeleted);
    }

    public async Task<List<Receipt>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _Context.Set<Receipt>()
            .Include(r => r.Customer)
            .Include(r => r.Cashier)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Currency)
            .Where(r => r.ReceiptDate >= startDate && 
                       r.ReceiptDate <= endDate && 
                       !r.IsDeleted)
            .OrderByDescending(r => r.ReceiptDate)
            .ToListAsync();
    }

    public async Task<List<Receipt>> GetByCashierIdAsync(int cashierId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _Context.Set<Receipt>()
            .Include(r => r.Customer)
            .Include(r => r.Cashier)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Currency)
            .Where(r => r.CashierId == cashierId && !r.IsDeleted);

        if (startDate.HasValue)
            query = query.Where(r => r.ReceiptDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(r => r.ReceiptDate <= endDate.Value);

        return await query
            .OrderByDescending(r => r.ReceiptDate)
            .ToListAsync();
    }

    public async Task<List<Receipt>> GetByCustomerIdAsync(int customerId)
    {
        return await _Context.Set<Receipt>()
            .Include(r => r.Cashier)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Currency)
            .Where(r => r.CustomerId == customerId && !r.IsDeleted)
            .OrderByDescending(r => r.ReceiptDate)
            .ToListAsync();
    }

    public async Task<List<Receipt>> GetByOrderIdAsync(int orderId)
    {
        return await _Context.Set<Receipt>()
            .Include(r => r.Customer)
            .Include(r => r.Cashier)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Currency)
            .Include(r => r.ReceiptItems)
                .ThenInclude(ri => ri.Product)
            .Include(r => r.ReceiptPayments)
            .Where(r => r.OrderId == orderId && !r.IsDeleted)
            .OrderByDescending(r => r.ReceiptDate)
            .ToListAsync();
    }

    public async Task<List<Receipt>> GetVoidReceiptsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _Context.Set<Receipt>()
            .Include(r => r.Customer)
            .Include(r => r.Cashier)
            .Include(r => r.VoidedByUser)
            .Where(r => r.IsVoid && !r.IsDeleted);

        if (startDate.HasValue)
            query = query.Where(r => r.VoidDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(r => r.VoidDate <= endDate.Value);

        return await query
            .OrderByDescending(r => r.VoidDate)
            .ToListAsync();
    }

    public async Task<decimal> GetTotalSalesByDateAsync(DateTime date)
    {
        var startOfDay = date.Date;
        var endOfDay = date.Date.AddDays(1).AddTicks(-1);

        return await _Context.Set<Receipt>()
            .Where(r => r.ReceiptDate >= startOfDay && 
                       r.ReceiptDate <= endOfDay && 
                       !r.IsDeleted && 
                       !r.IsVoid)
            .SumAsync(r => r.TotalAmount);
    }

    public async Task<decimal> GetTotalSalesByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _Context.Set<Receipt>()
            .Where(r => r.ReceiptDate >= startDate && 
                       r.ReceiptDate <= endDate && 
                       !r.IsDeleted && 
                       !r.IsVoid)
            .SumAsync(r => r.TotalAmount);
    }

    public async Task<int> GetReceiptCountByDateAsync(DateTime date)
    {
        var startOfDay = date.Date;
        var endOfDay = date.Date.AddDays(1).AddTicks(-1);

        return await _Context.Set<Receipt>()
            .Where(r => r.ReceiptDate >= startOfDay && 
                       r.ReceiptDate <= endOfDay && 
                       !r.IsDeleted && 
                       !r.IsVoid)
            .CountAsync();
    }

    public async Task<int> GetReceiptCountByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _Context.Set<Receipt>()
            .Where(r => r.ReceiptDate >= startDate && 
                       r.ReceiptDate <= endDate && 
                       !r.IsDeleted && 
                       !r.IsVoid)
            .CountAsync();
    }

    public async Task<List<Receipt>> GetRecentReceiptsAsync(int count = 10)
    {
        return await _Context.Set<Receipt>()
            .Include(r => r.Customer)
            .Include(r => r.Cashier)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Currency)
            .Where(r => !r.IsDeleted && !r.IsVoid)
            .OrderByDescending(r => r.ReceiptDate)
            .Take(count)
            .ToListAsync();
    }

    public async Task<string> GenerateNextReceiptNumberAsync()
    {
        var today = DateTime.Today;
        var startOfDay = today.Date;
        var endOfDay = today.Date.AddDays(1).AddTicks(-1);

        var lastReceiptNumber = await _Context.Set<Receipt>()
            .Where(r => r.ReceiptDate >= startOfDay && r.ReceiptDate <= endOfDay && !r.IsDeleted)
            .OrderByDescending(r => r.ReceiptNumber)
            .Select(r => r.ReceiptNumber)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(lastReceiptNumber))
        {
            return $"RCP-{today:yyyyMMdd}-001";
        }

        // Extract the sequence number and increment it
        var parts = lastReceiptNumber.Split('-');
        if (parts.Length >= 3 && int.TryParse(parts[2], out int sequenceNumber))
        {
            sequenceNumber++;
            return $"RCP-{today:yyyyMMdd}-{sequenceNumber:D3}";
        }

        return $"RCP-{today:yyyyMMdd}-001";
    }

    public async Task<List<Receipt>> SearchReceiptsAsync(string searchTerm)
    {
        var term = searchTerm.ToLower();
        
        return await _Context.Set<Receipt>()
            .Include(r => r.Customer)
            .Include(r => r.Cashier)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Currency)
            .Where(r => !r.IsDeleted && (
                r.ReceiptNumber.ToLower().Contains(term) ||
                (r.CustomerName != null && r.CustomerName.ToLower().Contains(term)) ||
                (r.CustomerPhone != null && r.CustomerPhone.Contains(term)) ||
                r.Customer.FirstName.ToLower().Contains(term) ||
                r.Customer.LastName.ToLower().Contains(term) ||
                r.Customer.Email.ToLower().Contains(term)
            ))
            .OrderByDescending(r => r.ReceiptDate)
            .Take(50) // Limit results for performance
            .ToListAsync();
    }

}
