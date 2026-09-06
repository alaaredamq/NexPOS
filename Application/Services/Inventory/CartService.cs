using Application.IServices.Base;
using Application.IServices.Inventory;
using Core.Entities.Inventory;
using Core.IRepositories.Inventory;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Inventory;

public class CartService : ICartService
{
    private readonly ICartRepository _cartRepo;
    private readonly ICartItemRepository _cartItemRepo;
    private readonly ICustomerCartRepository _customerCartRepo;
    private readonly ICurrencyConversionService _currencyService;
    private readonly ErpDbContext _dbContext;

    public CartService(ICartRepository cartRepo, ICartItemRepository cartItemRepo, ICustomerCartRepository customerCartRepo, ICurrencyConversionService currencyService, ErpDbContext dbContext)
    {
        _cartRepo = cartRepo;
        _cartItemRepo = cartItemRepo;
        _customerCartRepo = customerCartRepo;
        _currencyService = currencyService;
        _dbContext = dbContext;
    }

    public async Task<Cart> GetByIdAsync(int id)
    {
        return await _cartRepo.GetByIdAsync(id);
    }

    public async Task<Cart> GetOrCreateActiveCartAsync(int? customerId)
    {
        var activeCart = await _cartRepo.GetActiveCartByCustomerIdAsync(customerId);
        if (activeCart != null) return activeCart;
        
        return await CreateCartAsync(customerId);
    }

    public async Task<Cart?> GetActiveCartByCustomerIdAsync(int? customerId)
    {
        return await _cartRepo.GetActiveCartByCustomerIdAsync(customerId);
    }

    public async Task<List<Cart>> GetCartsByCustomerIdAsync(int? customerId)
    {
        return await _cartRepo.GetCartsByCustomerIdAsync(customerId);
    }

    public async Task<Cart> CreateCartAsync(int? customerId)
    {
        // Convert 0 to null for walk-in customers
        var effectiveCustomerId = customerId.HasValue && customerId.Value > 0 ? customerId : null;
        var cart = new Cart(effectiveCustomerId);
        return await _cartRepo.SaveAsync(cart);
    }

    public async Task<Cart> AddItemToCartAsync(int cartId, int productId, int quantity, decimal unitPrice, decimal? weightOrVolume = null)
    {
        // Never trust a caller-supplied price (S2) — reprice from the product's authoritative
        // SellingPrice. The unitPrice parameter is intentionally ignored; it stays on the
        // signature only because it's part of the public ICartService contract.
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted);
        if (product == null) throw new ArgumentException($"Product {productId} not found");

        return await AddItemToCartWithTrustedPriceAsync(cartId, productId, quantity, product.SellingPrice, weightOrVolume);
    }

    private async Task<Cart> AddItemToCartWithTrustedPriceAsync(int cartId, int productId, int quantity, decimal trustedUnitPrice, decimal? weightOrVolume = null)
    {
        var existingItem = await _cartItemRepo.GetByCartAndProductAsync(cartId, productId);

        if (existingItem != null)
        {
            existingItem.Quantity += quantity;
            existingItem.UnitPrice = trustedUnitPrice;
            // Update weight/volume if provided (for weight/volume products)
            if (weightOrVolume.HasValue)
            {
                existingItem.WeightOrVolume = (existingItem.WeightOrVolume ?? 0) + weightOrVolume.Value;
            }
            await _cartItemRepo.SaveAsync(existingItem);
        }
        else
        {
            var newItem = new CartItem(cartId, productId, quantity, trustedUnitPrice, weightOrVolume);
            await _cartItemRepo.SaveAsync(newItem);
        }

        return await _cartRepo.GetByIdAsync(cartId);
    }

    public async Task<Cart> AddItemsToCartAsync(int cartId, IReadOnlyCollection<CartItemInput> items)
    {
        if (items == null || items.Count == 0)
            return await _cartRepo.GetByIdAsync(cartId);

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();

        // One lookup for every product referenced, instead of one per item.
        var products = await _dbContext.Products
            .Where(p => productIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id);

        // One read of the cart's current items, keyed for merge-on-add.
        var existingItems = (await _cartItemRepo.GetByCartIdAsync(cartId))
            .GroupBy(ci => ci.ProductId)
            .ToDictionary(g => g.Key, g => g.First());

        var cartItems = _dbContext.Set<CartItem>();

        foreach (var input in items)
        {
            if (!products.TryGetValue(input.ProductId, out var product))
                throw new ArgumentException($"Product {input.ProductId} not found");

            // Never trust a caller-supplied price (S2) - reprice from SellingPrice.
            if (existingItems.TryGetValue(input.ProductId, out var existing))
            {
                existing.Quantity += input.Quantity;
                existing.UnitPrice = product.SellingPrice;
                if (input.WeightOrVolume.HasValue)
                    existing.WeightOrVolume = (existing.WeightOrVolume ?? 0) + input.WeightOrVolume.Value;
                cartItems.Update(existing);
            }
            else
            {
                var newItem = new CartItem(cartId, input.ProductId, input.Quantity, product.SellingPrice, input.WeightOrVolume);
                await cartItems.AddAsync(newItem);
                existingItems[input.ProductId] = newItem;
            }
        }

        // Single round trip for all inserts/updates, then one reload.
        await _dbContext.SaveChangesAsync();
        return await _cartRepo.GetByIdAsync(cartId);
    }

    public async Task<Cart> UpdateCartItemQuantityAsync(int cartId, int productId, int quantity, decimal? weightOrVolume = null)
    {
        return await UpdateCartItemQuantityInternalAsync(cartId, productId, quantity, trustedUnitPrice: null, weightOrVolume);
    }

    /// <summary>
    /// A12: shared implementation so a server-computed price (e.g. a currency conversion) can
    /// actually be applied when updating quantity, instead of being silently discarded like
    /// <see cref="UpdateCartItemQuantityWithCurrencyAsync"/> used to.
    /// </summary>
    private async Task<Cart> UpdateCartItemQuantityInternalAsync(int cartId, int productId, int quantity, decimal? trustedUnitPrice, decimal? weightOrVolume)
    {
        var item = await _cartItemRepo.GetByCartAndProductAsync(cartId, productId);
        if (item == null) throw new ArgumentException("Cart item not found");

        if (quantity <= 0)
        {
            await _cartItemRepo.DeleteAsync(item);
        }
        else
        {
            item.Quantity = quantity;
            if (trustedUnitPrice.HasValue)
            {
                item.UnitPrice = trustedUnitPrice.Value;
            }
            // Update weight/volume if provided (for weight/volume products)
            if (weightOrVolume.HasValue)
            {
                item.WeightOrVolume = weightOrVolume.Value;
            }
            await _cartItemRepo.SaveAsync(item);
        }

        return await _cartRepo.GetByIdAsync(cartId);
    }

    public async Task<Cart> RemoveItemFromCartAsync(int cartId, int productId)
    {
        var item = await _cartItemRepo.GetByCartAndProductAsync(cartId, productId);
        if (item != null)
        {
            await _cartItemRepo.DeleteAsync(item);
        }
        
        return await _cartRepo.GetByIdAsync(cartId);
    }

    public async Task<Cart> HoldCartAsync(int cartId)
    {
        var cart = await _cartRepo.GetByIdAsync(cartId);
        cart.IsActive = false;
        await _cartRepo.SaveAsync(cart);
        
        // Only create CustomerCart if there's a customer associated
        if (cart.CustomerId.HasValue && cart.CustomerId.Value > 0)
        {
            var customerCart = new CustomerCart(cart.CustomerId.Value, cartId);
            await _customerCartRepo.SaveAsync(customerCart);
        }
        
        return cart;
    }

    public async Task<Cart> ResumeCartAsync(int cartId)
    {
        var cart = await _cartRepo.GetByIdAsync(cartId);
        cart.IsActive = true;
        await _cartRepo.SaveAsync(cart);
        
        var customerCart = await _customerCartRepo.GetByCartIdAsync(cartId);
        if (customerCart != null)
        {
            customerCart.IsOnHold = false;
            await _customerCartRepo.SaveAsync(customerCart);
        }
        
        return cart;
    }

    public async Task<List<CustomerCart>> GetOnHoldCartsByCustomerIdAsync(int customerId)
    {
        return await _customerCartRepo.GetOnHoldCartsByCustomerIdAsync(customerId);
    }

    public async Task<List<Cart>> GetAllHeldCartsAsync()
    {
        // Get all carts that are held (not active and not deleted) from repository
        return await _cartRepo.GetAllHeldCartsAsync();
    }

    public async Task<decimal> CalculateCartTotalAsync(int cartId)
    {
        var items = await _cartItemRepo.GetByCartIdAsync(cartId);
        return items.Sum(item => item.TotalPrice);
    }

    public async Task<decimal> CalculateCartTotalInCurrencyAsync(int cartId, int targetCurrencyId)
    {
        var total = await CalculateCartTotalAsync(cartId);
        return await _currencyService.ConvertCartTotalAsync(total, cartId, targetCurrencyId);
    }

    public async Task<Cart> AddItemToCartWithCurrencyAsync(int cartId, int productId, int quantity, int targetCurrencyId)
    {
        // Get the product first to get its current price
        var product = await _dbContext.Products.FirstAsync(p => p.Id == productId);

        // Get product price in target currency
        var productPrice = await _currencyService.ConvertProductPriceAsync(product.SellingPrice, productId, targetCurrencyId);

        // Add item to cart with converted price. Must go through the trusted-price path
        // directly, not the public AddItemToCartAsync(cartId, productId, quantity, productPrice) -
        // that method now always re-derives the price from Product.SellingPrice itself (S2) and
        // would silently discard this currency conversion, charging the item's native-currency
        // price instead of the converted one.
        return await AddItemToCartWithTrustedPriceAsync(cartId, productId, quantity, productPrice);
    }

    public async Task<Cart> UpdateCartItemQuantityWithCurrencyAsync(int cartId, int productId, int quantity, int targetCurrencyId)
    {
        // Get current cart item to get the unit price. UnitPrice here is always
        // server-trusted (S2 guarantees it was set from Product.SellingPrice, never from a
        // client), so converting from it is safe.
        var cartItem = await _cartItemRepo.GetByCartAndProductAsync(cartId, productId);
        if (cartItem == null) throw new InvalidOperationException("Cart item not found");

        // Convert the unit price to target currency
        var convertedUnitPrice = await _currencyService.ConvertProductPriceAsync(cartItem.UnitPrice, productId, targetCurrencyId);

        // Update quantity and actually apply the converted price (A12 - this used to be
        // computed and silently discarded, leaving the item's price unconverted).
        return await UpdateCartItemQuantityInternalAsync(cartId, productId, quantity, convertedUnitPrice, weightOrVolume: null);
    }
}
