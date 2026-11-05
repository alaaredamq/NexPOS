using Application.IServices.Base;
using Core.Entities.Inventory;
using Core.Entities.Sales;
using Core.IRepositories.Sales;

namespace Application.IServices.Sales;

public interface IOrderService : IBaseService
{
    Task<Order> CreateOrderFromCartAsync(int cartId, int? customerId, decimal totalAmount, int? currencyId = null, int? cashDrawerSessionId = null, bool allowInsufficientStock = false);

    /// <summary>
    /// Same as <see cref="CreateOrderFromCartAsync(int,int?,decimal,int?,int?,bool)"/> but reuses a cart and
    /// its items that the caller has already loaded (with Product included), avoiding a re-fetch of the
    /// whole cart graph and a per-item product lookup on the checkout hot path.
    /// When <paramref name="allowInsufficientStock"/> is true the sale goes through even if stock is short
    /// (stock is floored at zero); otherwise an <c>InsufficientStockException</c> is thrown.
    /// </summary>
    Task<Order> CreateOrderFromCartAsync(Cart cart, IReadOnlyList<CartItem> cartItems, int? customerId, decimal totalAmount, int? currencyId = null, int? cashDrawerSessionId = null, bool allowInsufficientStock = false);
    Task<Order> GetByIdAsync(int id);
    Task<List<Order>> GetByCustomerIdAsync(int? customerId);
    Task<List<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    /// <summary>Revenue and gross profit of every completed order ever made.</summary>
    Task<LifetimeSalesTotals> GetLifetimeTotalsAsync();
    Task<Order> UpdateOrderStatusAsync(int orderId, int status);
}
