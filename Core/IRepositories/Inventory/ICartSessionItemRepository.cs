using Core.Entities.Inventory;
using Core.IRepositories.Base;

namespace Core.IRepositories.Inventory;

public interface ICartSessionItemRepository : IBaseRepository<CartSessionItem, int>
{
    Task<List<CartSessionItem>> GetByCartSessionIdAsync(int cartSessionId);
    Task<CartSessionItem?> GetByCartSessionAndProductAsync(int cartSessionId, int productId);
    Task DeleteByCartSessionIdAsync(int cartSessionId);
}
