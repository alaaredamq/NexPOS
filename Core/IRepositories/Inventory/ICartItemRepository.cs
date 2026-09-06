using Core.Entities.Inventory;
using Core.IRepositories.Base;

namespace Core.IRepositories.Inventory;

public interface ICartItemRepository : IRepository<CartItem>
{
    Task<CartItem?> GetByCartAndProductAsync(int cartId, int productId);
    Task<List<CartItem>> GetByCartIdAsync(int cartId);
}
