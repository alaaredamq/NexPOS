using System.Globalization;
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
            .Include(r => r.Currency)
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

    public Task<string> GenerateNextReceiptNumberAsync() => GenerateNextReceiptNumberAsync(DateTime.Today);

    /// <summary>
    /// The next free "RCP-yyyyMMdd-NNN" for the given local date: one more than the highest sequence already issued
    /// with that prefix.
    /// <para>
    /// It goes by the numbers themselves, never by <c>ReceiptDate</c>. The number carries the LOCAL date but
    /// <c>ReceiptDate</c> is stored in UTC, so a "today's receipts" date window misses the receipts issued in the hours
    /// around local midnight (00:00-03:00 in UTC+3), hands out the same number twice, and the insert dies on the unique
    /// index on <c>ReceiptNumber</c> - which left the sale with no receipt. Deleted and void receipts keep their numbers
    /// (the index covers every row), so they count too.
    /// </para>
    /// </summary>
    public async Task<string> GenerateNextReceiptNumberAsync(DateTime today)
    {
        var prefix = $"RCP-{today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-";

        var issued = await _Context.Set<Receipt>()
            .Where(r => r.ReceiptNumber.StartsWith(prefix))
            .Select(r => r.ReceiptNumber)
            .ToListAsync();

        // Numeric, so "...-1000" follows "...-999"; anything not ending in a plain number is ignored.
        var highest = issued
            .Select(number => int.TryParse(number.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
                ? sequence
                : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highest + 1:D3}";
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
