using Core.Entities.Users;
using Core.IRepositories.Base;

namespace Core.IRepositories.Users;

public interface ICustomerRepository : IRepository<Customer>
{
    /// <summary>All customers with their <c>Orders</c> loaded, for list views that show order count / total spent.</summary>
    Task<List<Customer>> GetAllWithOrdersAsync();
}


