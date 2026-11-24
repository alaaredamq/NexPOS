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

    /// <summary>
    /// The customers whose phone number is the same number as <paramref name="phone"/>, however either is written ("+961 70 123 456",
    /// "70-123456", "0096170123456"): see <see cref="Core.Entities.Users.PhoneMatch"/>. Usually one; several when a number was shared.
    /// </summary>
    Task<List<Customer>> FindByPhoneAsync(string phone);

    /// <summary>
    /// Purchases per customer id, without loading any order: how many orders and their summed <c>TotalAmount</c>. Cancelled and
    /// returned orders (a voided receipt) are no longer purchases and are left out. Customers without orders have no entry.
    /// </summary>
    Task<Dictionary<int, (int OrdersCount, decimal TotalSpent)>> GetPurchaseTotalsAsync();
}


