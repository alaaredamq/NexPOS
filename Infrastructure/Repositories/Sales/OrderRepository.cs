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
