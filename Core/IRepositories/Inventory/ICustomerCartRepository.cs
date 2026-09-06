using Core.Entities.Inventory;
using Core.IRepositories.Base;

namespace Core.IRepositories.Inventory;

public interface ICustomerCartRepository : IRepository<CustomerCart>
{
    Task<List<CustomerCart>> GetOnHoldCartsByCustomerIdAsync(int customerId);
    Task<CustomerCart?> GetByCartIdAsync(int cartId);
}
