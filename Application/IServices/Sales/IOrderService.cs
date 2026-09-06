using Application.IServices.Base;
using Core.Entities.Inventory;
using Core.Entities.Sales;

namespace Application.IServices.Sales;

public interface IOrderService : IBaseService
{
    Task<Order> CreateOrderFromCartAsync(int cartId, int? customerId, decimal totalAmount, int? currencyId = null, int? cashDrawerSessionId = null);

    /// <summary>
    /// Same as <see cref="CreateOrderFromCartAsync(int,int?,decimal,int?,int?)"/> but reuses a cart and
    /// its items that the caller has already loaded (with Product included), avoiding a re-fetch of the
    /// whole cart graph and a per-item product lookup on the checkout hot path.
    /// </summary>
    Task<Order> CreateOrderFromCartAsync(Cart cart, IReadOnlyList<CartItem> cartItems, int? customerId, decimal totalAmount, int? currencyId = null, int? cashDrawerSessionId = null);
    Task<Order> GetByIdAsync(int id);
    Task<List<Order>> GetByCustomerIdAsync(int? customerId);
    Task<List<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Order> UpdateOrderStatusAsync(int orderId, int status);
}
