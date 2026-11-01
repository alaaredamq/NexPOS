using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Sales;

public class OrderRepository : Repository<Order>, IOrderRepository
{
    public OrderRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public new async Task<Order> GetByIdAsync(int id)
    {
        var order = await GetAllQueryable()
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .Include(o => o.Customer)
            .Include(o => o.Currency)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
            throw new ArgumentNullException($"Order with id {id} not found");

        return order;
    }

    public async Task<HashSet<int>> GetSoldCartIdsAsync(IReadOnlyCollection<int> cartIds)
    {
        if (cartIds.Count == 0) return new HashSet<int>();

        // IgnoreQueryFilters: a deleted order still owns its cart; the cart must never be mistaken for a parked one.
        var sold = new HashSet<int>();
        foreach (var chunk in cartIds.Chunk(500)) // SQLite caps the number of parameters
            sold.UnionWith(await _Context.Set<Order>().IgnoreQueryFilters().Where(o => chunk.Contains(o.CartId)).Select(o => o.CartId).ToListAsync());
        return sold;
    }

    public async Task<List<Order>> GetByCustomerIdAsync(int customerId)
    {
        return await GetAllQueryable()
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .Include(o => o.Customer)
            .Include(o => o.Currency)
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();
    }

    public async Task<List<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await GetAllQueryable()
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .Include(o => o.Customer)
            .Include(o => o.Currency)
            .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDate)
            .ToListAsync();
    }
}
