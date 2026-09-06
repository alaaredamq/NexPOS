using Application.IServices.Base;
using Core.Entities.Inventory;

namespace Application.IServices.Inventory;

public interface ICartService : IBaseService
{
    Task<Cart> GetByIdAsync(int id);
    Task<Cart> GetOrCreateActiveCartAsync(int? customerId);
    Task<Cart?> GetActiveCartByCustomerIdAsync(int? customerId);
    Task<List<Cart>> GetCartsByCustomerIdAsync(int? customerId);
    Task<Cart> CreateCartAsync(int? customerId);
    Task<Cart> AddItemToCartAsync(int cartId, int productId, int quantity, decimal unitPrice, decimal? weightOrVolume = null);

    /// <summary>
    /// Adds several items to a cart in a single unit of work - one product lookup, one existing-items
    /// read, one SaveChanges, one cart reload - instead of paying that round-trip cost per item.
    /// Prices are always re-derived from Product.SellingPrice (S2), same as <see cref="AddItemToCartAsync"/>.
    /// </summary>
    Task<Cart> AddItemsToCartAsync(int cartId, IReadOnlyCollection<CartItemInput> items);
    Task<Cart> UpdateCartItemQuantityAsync(int cartId, int productId, int quantity, decimal? weightOrVolume = null);
    Task<Cart> RemoveItemFromCartAsync(int cartId, int productId);
    Task<Cart> HoldCartAsync(int cartId);
    Task<Cart> ResumeCartAsync(int cartId);
    Task<List<CustomerCart>> GetOnHoldCartsByCustomerIdAsync(int customerId);
    Task<List<Cart>> GetAllHeldCartsAsync();
    Task<decimal> CalculateCartTotalAsync(int cartId);
    Task<decimal> CalculateCartTotalInCurrencyAsync(int cartId, int targetCurrencyId);
    Task<Cart> AddItemToCartWithCurrencyAsync(int cartId, int productId, int quantity, int targetCurrencyId);
    Task<Cart> UpdateCartItemQuantityWithCurrencyAsync(int cartId, int productId, int quantity, int targetCurrencyId);
}

/// <summary>One line item to add via <see cref="ICartService.AddItemsToCartAsync"/>.</summary>
public record CartItemInput(int ProductId, int Quantity, decimal? WeightOrVolume = null);
