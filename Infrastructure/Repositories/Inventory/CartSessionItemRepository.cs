using Core.Entities.Inventory;
using Core.IRepositories.Inventory;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Repositories.Inventory;

public class CartSessionItemRepository : BaseRepository<CartSessionItem, int>, ICartSessionItemRepository
{
    public CartSessionItemRepository(ErpDbContext context, IServiceProvider serviceProvider) 
        : base(context, serviceProvider)
    {
    }

    public async Task<List<CartSessionItem>> GetByCartSessionIdAsync(int cartSessionId)
    {
        return await _Entities
            .Include(ci => ci.Product)
            .Where(ci => ci.CartSessionId == cartSessionId)
            .ToListAsync();
    }

    public async Task<CartSessionItem?> GetByCartSessionAndProductAsync(int cartSessionId, int productId)
    {
        return await _Entities
            .Include(ci => ci.Product)
            .FirstOrDefaultAsync(ci => ci.CartSessionId == cartSessionId && ci.ProductId == productId);
    }

    public async Task DeleteByCartSessionIdAsync(int cartSessionId)
    {
        var items = await _Entities.Where(ci => ci.CartSessionId == cartSessionId).ToListAsync();
        foreach (var item in items)
        {
            await DeleteAsync(item);
        }
    }
}
