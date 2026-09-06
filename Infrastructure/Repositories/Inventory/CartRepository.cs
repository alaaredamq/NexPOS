using Core.Entities.Inventory;
using Core.IRepositories.Inventory;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Inventory;

public class CartRepository : Repository<Cart>, ICartRepository
{
    public CartRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public new async Task<Cart> GetByIdAsync(int id)
    {
        var cart = await GetAllQueryable()
            .Include(c => c.Customer)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Category)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Currency)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (cart == null)
            throw new ArgumentNullException($"Cart with id {id} not found");

        return cart;
    }

    public async Task<Cart?> GetActiveCartByCustomerIdAsync(int? customerId)
    {
        return await GetAllQueryable()
            .Include(c => c.Customer)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Category)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Currency)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.IsActive);
    }

    public async Task<List<Cart>> GetCartsByCustomerIdAsync(int? customerId)
    {
        return await GetAllQueryable()
            .Include(c => c.Customer)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Category)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Currency)
            .Where(c => c.CustomerId == customerId)
            .ToListAsync();
    }

    public async Task<List<Cart>> GetAllHeldCartsAsync()
    {
        return await GetAllQueryable()
            .Include(c => c.Customer)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Category)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Currency)
            .Where(c => !c.IsActive && !c.IsDeleted)
            .ToListAsync();
    }
}
