using Application.IServices.Inventory;
using Core.Entities.Inventory;
using Core.IRepositories.Inventory;
using Microsoft.Extensions.Logging;

namespace Application.Services.Inventory;

public class CartSessionService : ICartSessionService
{
    private readonly ICartSessionRepository _cartSessionRepository;
    private readonly ICartSessionItemRepository _cartSessionItemRepository;
    private readonly ILogger<CartSessionService> _logger;

    public CartSessionService(
        ICartSessionRepository cartSessionRepository,
        ICartSessionItemRepository cartSessionItemRepository,
        ILogger<CartSessionService> logger)
    {
        _cartSessionRepository = cartSessionRepository;
        _cartSessionItemRepository = cartSessionItemRepository;
        _logger = logger;
    }

    public async Task<CartSession> GetOrCreateCartAsync(string sessionId, int? customerId = null)
    {
        try
        {
            return await _cartSessionRepository.CreateOrGetAsync(sessionId, customerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating cart for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<CartSession> AddItemToCartAsync(string sessionId, int productId, int quantity, decimal unitPrice, decimal? weightOrVolume = null)
    {
        try
        {
            var cart = await GetOrCreateCartAsync(sessionId);
            var existingItem = await _cartSessionItemRepository.GetByCartSessionAndProductAsync(cart.Id, productId);
            
            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
                // Update weight/volume if provided (for weight/volume products)
                if (weightOrVolume.HasValue)
                {
                    existingItem.WeightOrVolume = (existingItem.WeightOrVolume ?? 0) + weightOrVolume.Value;
                }
                await _cartSessionItemRepository.SaveAsync(existingItem);
            }
            else
            {
                var newItem = new CartSessionItem(cart.Id, productId, quantity, unitPrice, weightOrVolume);
                await _cartSessionItemRepository.SaveAsync(newItem);
            }
            
            return await _cartSessionRepository.GetByIdAsync(cart.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding item to cart for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<CartSession> UpdateItemQuantityAsync(string sessionId, int productId, int quantity, decimal? weightOrVolume = null)
    {
        try
        {
            var cart = await GetOrCreateCartAsync(sessionId);
            var item = await _cartSessionItemRepository.GetByCartSessionAndProductAsync(cart.Id, productId);
            
            if (item == null)
                throw new ArgumentException("Cart item not found");
            
            if (quantity <= 0)
            {
                await _cartSessionItemRepository.DeleteAsync(item);
            }
            else
            {
                item.Quantity = quantity;
                // Update weight/volume if provided (for weight/volume products)
                if (weightOrVolume.HasValue)
                {
                    item.WeightOrVolume = weightOrVolume.Value;
                }
                await _cartSessionItemRepository.SaveAsync(item);
            }
            
            return await _cartSessionRepository.GetByIdAsync(cart.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating item quantity for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<CartSession> RemoveItemFromCartAsync(string sessionId, int productId)
    {
        try
        {
            var cart = await GetOrCreateCartAsync(sessionId);
            var item = await _cartSessionItemRepository.GetByCartSessionAndProductAsync(cart.Id, productId);
            
            if (item != null)
            {
                await _cartSessionItemRepository.DeleteAsync(item);
            }
            
            return await _cartSessionRepository.GetByIdAsync(cart.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing item from cart for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<CartSession> ClearCartAsync(string sessionId)
    {
        try
        {
            var cart = await GetOrCreateCartAsync(sessionId);
            await _cartSessionItemRepository.DeleteByCartSessionIdAsync(cart.Id);
            
            // Reset discount
            cart.DiscountAmount = null;
            cart.DiscountCode = null;
            cart.DiscountPercentage = null;
            await _cartSessionRepository.SaveAsync(cart);
            
            return await _cartSessionRepository.GetByIdAsync(cart.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cart for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<CartSession> ApplyDiscountAsync(string sessionId, decimal? discountAmount = null, string? discountCode = null, decimal? discountPercentage = null)
    {
        try
        {
            var cart = await GetOrCreateCartAsync(sessionId);
            
            cart.DiscountAmount = discountAmount;
            cart.DiscountCode = discountCode;
            cart.DiscountPercentage = discountPercentage;
            
            await _cartSessionRepository.SaveAsync(cart);
            
            return await _cartSessionRepository.GetByIdAsync(cart.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying discount for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<decimal> CalculateCartTotalAsync(string sessionId)
    {
        try
        {
            var cart = await GetOrCreateCartAsync(sessionId);
            var items = await _cartSessionItemRepository.GetByCartSessionIdAsync(cart.Id);
            
            var subtotal = items.Sum(item => item.TotalPrice);
            var total = subtotal;
            
            // Apply discount
            if (cart.DiscountPercentage.HasValue)
            {
                total = subtotal * (1 - cart.DiscountPercentage.Value / 100);
            }
            else if (cart.DiscountAmount.HasValue)
            {
                total = Math.Max(0, subtotal - cart.DiscountAmount.Value);
            }
            
            return total;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating cart total for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<CartSession?> GetCartAsync(string sessionId)
    {
        try
        {
            return await _cartSessionRepository.GetActiveBySessionIdAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<List<CartSessionItem>> GetCartItemsAsync(string sessionId)
    {
        try
        {
            var cart = await GetOrCreateCartAsync(sessionId);
            return await _cartSessionItemRepository.GetByCartSessionIdAsync(cart.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart items for session {SessionId}", sessionId);
            throw;
        }
    }
}
