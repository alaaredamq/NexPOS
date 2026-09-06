using Core.Entities.Sales;
using Core.IRepositories.Base;

namespace Core.IRepositories.Sales;

public interface IOrderRepository : IRepository<Order>
{
    Task<List<Order>> GetByCustomerIdAsync(int customerId);
    Task<List<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
}
