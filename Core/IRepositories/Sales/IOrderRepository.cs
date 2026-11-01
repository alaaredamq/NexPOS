using Core.Entities.Sales;
using Core.IRepositories.Base;

namespace Core.IRepositories.Sales;

public interface IOrderRepository : IRepository<Order>
{
    Task<List<Order>> GetByCustomerIdAsync(int customerId);
    Task<List<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);

    /// <summary>Which of these carts were sold (have an order, deleted or not). A light query: no order graph is loaded.</summary>
    Task<HashSet<int>> GetSoldCartIdsAsync(IReadOnlyCollection<int> cartIds);
}
