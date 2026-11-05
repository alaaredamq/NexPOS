using Core.Entities.Sales;
using Core.IRepositories.Base;

namespace Core.IRepositories.Sales;

public interface IOrderRepository : IRepository<Order>
{
    Task<List<Order>> GetByCustomerIdAsync(int customerId);
    Task<List<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);

    /// <summary>The orders matching the filter as light list rows (no cart items or products loaded), newest first. Deleted orders are left out.</summary>
    Task<List<OrderListRow>> GetListRowsAsync(OrderListFilter filter);

    /// <summary>
    /// Revenue (order totals) and gross profit ((price - cost price) x quantity over the lines whose product has a cost price) of every completed order.
    /// A light query: only the amounts are read, not the order graph.
    /// </summary>
    Task<LifetimeSalesTotals> GetLifetimeTotalsAsync();

    /// <summary>Which of these carts were sold (have an order, deleted or not). A light query: no order graph is loaded.</summary>
    Task<HashSet<int>> GetSoldCartIdsAsync(IReadOnlyCollection<int> cartIds);
}
