using Core.Entities.Inventory;
using Core.IRepositories.Inventory;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Inventory;

public class CartItemRepository : Repository<CartItem>, ICartItemRepository
{
    public CartItemRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<CartItem?> GetByCartAndProductAsync(int cartId, int productId)
    {
        return await GetAllQueryable()
            .Include(ci => ci.Product)
            .FirstOrDefaultAsync(ci => ci.CartId == cartId && ci.ProductId == productId);
    }

    public async Task<List<CartItem>> GetByCartIdAsync(int cartId)
    {
        return await GetAllQueryable()
            .Include(ci => ci.Product)
            .Where(ci => ci.CartId == cartId)
            .ToListAsync();
    }
}
