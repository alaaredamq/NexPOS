using Core.Entities.Users;
using Core.IRepositories.Base;

namespace Core.IRepositories.Users;

public interface ICustomerRepository : IRepository<Customer>
{
    /// <summary>All customers with their <c>Orders</c> loaded, for list views that show order count / total spent.</summary>
    Task<List<Customer>> GetAllWithOrdersAsync();

    /// <summary>
    /// One page of customers ordered by name, optionally filtered by name / email / phone. Returns up to
    /// <paramref name="take"/> items plus whether more follow (fetches take + 1, so no COUNT query).
    /// </summary>
    Task<(List<Customer> Items, bool HasMore)> SearchPagedAsync(string? search, int skip, int take);
}


