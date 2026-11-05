using Application.Exceptions;
using Application.IServices.Licensing;
using Application.IServices.Sales;
using Application.IServices.Notifications;
using Application.IServices.Products;
using Core.Entities.Inventory;
using Core.Entities.Products;
using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Core.IRepositories.Inventory;
using Core.IRepositories.Products;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepo;
    private readonly ICartRepository _cartRepo;
    private readonly ICartItemRepository _cartItemRepo;
    private readonly IProductRepository _productRepo;
    private readonly IStockAlertService _stockAlertService;
    private readonly IProductHistoryService _history;
    private readonly ILicenseService _licenseService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepo,
        ICartRepository cartRepo,
        ICartItemRepository cartItemRepo,
        IProductRepository productRepo,
        IStockAlertService stockAlertService,
        IProductHistoryService history,
        ILicenseService licenseService,
        ILogger<OrderService> logger)
    {
        _orderRepo = orderRepo;
        _cartRepo = cartRepo;
        _cartItemRepo = cartItemRepo;
        _productRepo = productRepo;
        _stockAlertService = stockAlertService;
        _history = history;
        _licenseService = licenseService;
        _logger = logger;
    }

    public async Task<Order> CreateOrderFromCartAsync(int cartId, int? customerId, decimal totalAmount, int? currencyId = null, int? cashDrawerSessionId = null, bool allowInsufficientStock = false)
    {
        var cart = await _cartRepo.GetByIdAsync(cartId);
        if (cart == null) throw new ArgumentException("Cart not found");

        var cartItems = await _cartItemRepo.GetByCartIdAsync(cartId);
        return await CreateOrderFromCartAsync(cart, cartItems, customerId, totalAmount, currencyId, cashDrawerSessionId, allowInsufficientStock);
    }

    public async Task<Order> CreateOrderFromCartAsync(Cart cart, IReadOnlyList<CartItem> cartItems, int? customerId, decimal totalAmount, int? currencyId = null, int? cashDrawerSessionId = null, bool allowInsufficientStock = false)
    {
        // Every new order in the system comes through here, so this is the last line of defence
        // for the license rule (see ILicenseService).
        _licenseService.EnsureSalesAllowed();

        if (cart == null) throw new ArgumentException("Cart not found");
        if (cartItems == null || !cartItems.Any())
        {
            throw new InvalidOperationException("Cannot create order from empty cart");
        }

        // Track product IDs for alert checking after all saves are done
        var productIdsToCheck = new List<int>();

        // What each sale line did to stock, kept until the order has an id to point the history at.
        var soldLines = new List<(int ProductId, decimal Quantity, decimal? StockBefore, decimal? StockAfter)>();

        // Deduct inventory for each item. cartItems already carry their Product (included by the
        // cart/cart-item repositories), so there's no per-item product round trip here.
        foreach (var cartItem in cartItems)
        {
            var product = cartItem.Product ?? await _productRepo.GetByIdAsync(cartItem.ProductId);
            if (product == null)
            {
                _logger.LogWarning("Product {ProductId} not found when deducting inventory", cartItem.ProductId);
                continue;
            }

            // For weight/volume products, use WeightOrVolume; for piece products, use Quantity
            decimal quantityToDeduct = cartItem.WeightOrVolume.HasValue && cartItem.WeightOrVolume.Value > 0
                ? cartItem.WeightOrVolume.Value
                : cartItem.Quantity;

            // Atomic check-and-decrement (single UPDATE ... WHERE StockQuantity >= quantity) so two
            // concurrent sales of the same product can't both pass a stale in-memory stock check.
            var deducted = await _productRepo.TryDeductStockAsync(product.Id, quantityToDeduct);
            // A normal sale takes exactly what was sold, so the history derives "before" from the stock after it;
            // a sale past the available stock floors at zero, so there "before" is what was on the shelf.
            decimal? stockBefore = null;
            if (!deducted)
            {
                if (!allowInsufficientStock)
                {
                    _logger.LogWarning("Insufficient stock for product {ProductId} ({ProductName}). Requested: {Requested}",
                        product.Id, product.Name, quantityToDeduct);
                    throw new InsufficientStockException($"Insufficient stock for {product.Name}. Requested {quantityToDeduct}, available {product.StockQuantity}.");
                }

                _logger.LogWarning("Selling product {ProductId} ({ProductName}) past available stock by cashier override. Requested: {Requested}, available: {Available}",
                    product.Id, product.Name, quantityToDeduct, product.StockQuantity);
                stockBefore = await _productRepo.GetStockQuantityAsync(product.Id);
                await _productRepo.ForceDeductStockAsync(product.Id, quantityToDeduct);
            }

            soldLines.Add((product.Id, quantityToDeduct, stockBefore, await _productRepo.GetStockQuantityAsync(product.Id)));

            _logger.LogDebug("Deducted {Quantity} {Unit} of product {ProductId} ({ProductName})",
                quantityToDeduct, product.Unit ?? "units", product.Id, product.Name);

            productIdsToCheck.Add(product.Id);
        }

        // For POS sales, orders are immediately completed (Delivered status)
        // Customer walks out with products - no pending/processing/shipping needed
        var order = new Order(customerId, cart.Id, totalAmount, (int)Core.Enum.OrderStatus.Delivered, currencyId);

        // Link to cash drawer session if provided
        if (cashDrawerSessionId.HasValue)
        {
            order.CashDrawerSessionId = cashDrawerSessionId.Value;
        }

        var createdOrder = await _orderRepo.SaveAsync(order);

        // Checkout runs this inside its transaction, so a sale that rolls back takes its history lines with it.
        foreach (var line in soldLines)
            await _history.RecordMovementAsync(line.ProductId, ProductHistoryType.Sold, -line.Quantity, orderId: createdOrder.Id, stockBefore: line.StockBefore, stockAfter: line.StockAfter);

        // Deactivate the cart after creating order
        cart.IsActive = false;
        await _cartRepo.SaveAsync(cart);

        _logger.LogInformation("Order {OrderId} created successfully from cart {CartId}. Inventory deducted for {ItemCount} items",
            createdOrder.Id, cart.Id, cartItems.Count);

        // Check for stock alerts after all database operations are complete
        // This prevents DbContext threading issues
        foreach (var productId in productIdsToCheck)
        {
            try
            {
                await _stockAlertService.CheckProductStockAfterSaleAsync(productId);
            }
            catch (Exception alertEx)
            {
                _logger.LogError(alertEx, "Error creating stock alert for product {ProductId}", productId);
                // Don't fail order if alert creation fails
            }
        }

        return createdOrder;
    }

    public async Task<Order> GetByIdAsync(int id)
    {
        return await _orderRepo.GetByIdAsync(id);
    }

    public async Task<List<Order>> GetByCustomerIdAsync(int? customerId)
    {
        if (!customerId.HasValue) return new List<Order>();
        return await _orderRepo.GetByCustomerIdAsync(customerId.Value);
    }

    public async Task<List<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _orderRepo.GetByDateRangeAsync(startDate, endDate);
    }

    public Task<LifetimeSalesTotals> GetLifetimeTotalsAsync() => _orderRepo.GetLifetimeTotalsAsync();

    public async Task<Order> UpdateOrderStatusAsync(int orderId, int status)
    {
        var order = await _orderRepo.GetByIdAsync(orderId);
        if (order.Status == status) return order;

        // Cancelling is not just a status number - stock, receipt, debt and drawer all have to follow -
        // so it only happens through ISaleReversalService, and a cancelled order can't be reopened
        // (its stock was returned, so it would be counted as sold again without being taken back out).
        if (status == (int)Core.Enum.OrderStatus.Cancelled)
            throw new InvalidOperationException("An order is cancelled through the sale reversal, not by setting its status.");
        if (order.Status == (int)Core.Enum.OrderStatus.Cancelled)
            throw new InvalidOperationException("A cancelled order cannot be reopened.");

        order.Status = status;
        return await _orderRepo.SaveAsync(order);
    }
}
