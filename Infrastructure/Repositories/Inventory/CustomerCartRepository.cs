using Core.Entities.Inventory;
using Core.IRepositories.Inventory;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Inventory;

public class CustomerCartRepository : Repository<CustomerCart>, ICustomerCartRepository
{
    public CustomerCartRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<List<CustomerCart>> GetOnHoldCartsByCustomerIdAsync(int customerId)
    {
        return await GetAllQueryable()
            .Include(cc => cc.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .Where(cc => cc.CustomerId == customerId && cc.IsOnHold)
            .ToListAsync();
    }

    public async Task<CustomerCart?> GetByCartIdAsync(int cartId)
    {
        return await GetAllQueryable()
            .Include(cc => cc.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefaultAsync(cc => cc.CartId == cartId);
    }
}
