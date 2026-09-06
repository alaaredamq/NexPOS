using Core.Entities.Inventory;
using Core.IRepositories.Base;

namespace Core.IRepositories.Inventory;

public interface ICartRepository : IRepository<Cart>
{
    Task<Cart?> GetActiveCartByCustomerIdAsync(int? customerId);
    Task<List<Cart>> GetCartsByCustomerIdAsync(int? customerId);
    Task<List<Cart>> GetAllHeldCartsAsync();
}
