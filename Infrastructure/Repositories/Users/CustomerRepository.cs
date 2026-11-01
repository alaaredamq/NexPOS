using Core.Entities.Users;
using Core.IRepositories.Users;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Users;

public class CustomerRepository : Repository<Customer>, ICustomerRepository
{
    public CustomerRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<List<Customer>> GetAllWithOrdersAsync() =>
        await GetAllQueryable().Include(c => c.Orders).AsSplitQuery().ToListAsync();

    public async Task<(List<Customer> Items, bool HasMore)> SearchPagedAsync(string? search, int skip, int take)
    {
        var query = GetAllQueryable();

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(c =>
                (c.FirstName + " " + c.LastName).Contains(term) ||
                c.Email.Contains(term) ||
                (c.PhoneNumber != null && c.PhoneNumber.Contains(term)));
        }

        var rows = await query
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName).ThenBy(c => c.Id)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync();

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(take);
        return (rows, hasMore);
    }

    public async Task<Dictionary<int, (int OrdersCount, decimal TotalSpent)>> GetPurchaseTotalsAsync()
    {
        var cancelled = (int)Core.Enum.OrderStatus.Cancelled;
        var returned = (int)Core.Enum.OrderStatus.Returned;

        // Only the two columns needed. Summed here, not in SQL: SQLite stores decimals as text/real and cannot SUM them exactly.
        var rows = await _Context.Set<Core.Entities.Sales.Order>()
            .Where(o => o.CustomerId != null && o.Status != cancelled && o.Status != returned)
            .Select(o => new { CustomerId = o.CustomerId!.Value, o.TotalAmount })
            .ToListAsync();

        return rows
            .GroupBy(r => r.CustomerId)
            .ToDictionary(g => g.Key, g => (g.Count(), g.Sum(r => r.TotalAmount)));
    }

    public new async Task<Customer> GetByIdAsync(int id)
    {
        var customer = await GetAllQueryable()
            .Include(c => c.Orders)
                .ThenInclude(o => o.Currency)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null)
            throw new ArgumentNullException($"Customer with id {id} not found");

        return customer;
    }
}


