using Core.Entities.Inventory;

namespace Application.IServices.Inventory;

public interface ICartSessionService
{
    Task<CartSession> GetOrCreateCartAsync(string sessionId, int? customerId = null);
    Task<CartSession> AddItemToCartAsync(string sessionId, int productId, int quantity, decimal unitPrice, decimal? weightOrVolume = null);
    Task<CartSession> UpdateItemQuantityAsync(string sessionId, int productId, int quantity, decimal? weightOrVolume = null);
    Task<CartSession> RemoveItemFromCartAsync(string sessionId, int productId);
    Task<CartSession> ClearCartAsync(string sessionId);
    Task<CartSession> ApplyDiscountAsync(string sessionId, decimal? discountAmount = null, string? discountCode = null, decimal? discountPercentage = null);
    Task<decimal> CalculateCartTotalAsync(string sessionId);
    Task<CartSession?> GetCartAsync(string sessionId);
    Task<List<CartSessionItem>> GetCartItemsAsync(string sessionId);
}
