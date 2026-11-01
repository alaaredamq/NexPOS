using Application.IServices.Inventory;
using Application.IServices.Products;
using Core.Entities.Inventory;
using Core.IRepositories.Inventory;
using Core.IRepositories.Sales;
using Web.Api.Carts.Contracts;
using Web.Api.Pos;

namespace Web.Api.Carts;

/// <summary>Parked ("held") carts. Stored exactly as the Razor till stores them, so both tills can see and resume each other's during the migration.</summary>
public interface IHeldCartService
{
    Task<HeldCartDto> HoldAsync(PosHoldCartRequest request);

    Task<IReadOnlyList<HeldCartDto>> ListAsync();

    /// <summary>The cart as the till needs it to carry on. The held cart itself is left in place (the cashier deletes it when done), as before.</summary>
    Task<ResumedCartDto> GetAsync(int id);

    Task DeleteAsync(int id);

    /// <returns>How many carts were deleted.</returns>
    Task<int> DeleteAllAsync();
}

public sealed class HeldCartService : IHeldCartService
{
    /// <summary>The marker the old till writes in front of the notes of a walk-in cart (<c>Cart.DiscountCode</c> doubles as the note field).</summary>
    private const string WalkInMarker = "__WALKIN__";

    private readonly IPosPricingService _pricing;
    private readonly ICartService _carts;
    private readonly ICartRepository _cartRepo;
    private readonly IOrderRepository _orders;
    private readonly IProductPriceService _prices;

    public HeldCartService(IPosPricingService pricing, ICartService carts, ICartRepository cartRepo, IOrderRepository orders, IProductPriceService prices)
    {
        _pricing = pricing;
        _carts = carts;
        _cartRepo = cartRepo;
        _orders = orders;
        _prices = prices;
    }

    public async Task<HeldCartDto> HoldAsync(PosHoldCartRequest request)
    {
        // The same checks a sale gets (unknown product, a price that is not the product's, a custom price below cost, a bad discount),
        // so a parked cart can always be resumed and sold.
        await _pricing.PriceDetailedAsync(request.Cart);

        var customerId = request.CustomerId is > 0 ? request.CustomerId : null;
        var isWalkIn = customerId is null;

        var cart = await _carts.CreateCartAsync(customerId);
        try
        {
            cart = await _carts.AddItemsToCartAsync(cart.Id, request.Cart.Lines
                .Select(l => new CartItemInput(l.ProductId, l.Quantity, l.WeightOrVolume, l.PriceTierId, l.CustomPrice, request.Cart.CurrencyId))
                .ToList());
        }
        catch (ArgumentException ex)
        {
            await SoftDeleteAsync(cart.Id);
            throw PosRuleException.Raw(StatusCodes.Status400BadRequest, "validation_failed", ex.Message, "lines");
        }

        cart.DiscountAmount = request.Cart.DiscountPercentage is > 0 ? null : request.Cart.DiscountAmount;
        cart.DiscountPercentage = request.Cart.DiscountPercentage;
        cart.DiscountCode = request.Cart.DiscountLabel?.Trim();

        var notes = request.Notes?.Trim() ?? string.Empty;
        if (isWalkIn) notes = WalkInMarker + notes;
        if (notes.Length > 0)
            cart.DiscountCode = string.IsNullOrEmpty(cart.DiscountCode) ? notes : $"{cart.DiscountCode} | {notes}";
        if (string.IsNullOrEmpty(cart.DiscountCode)) cart.DiscountCode = null;

        await _cartRepo.SaveAsync(cart);
        await _carts.HoldCartAsync(cart.Id);

        return Map(await _carts.GetByIdAsync(cart.Id));
    }

    public async Task<IReadOnlyList<HeldCartDto>> ListAsync()
        => (await HeldCartsAsync()).OrderByDescending(c => c.CreatedAt).Select(Map).ToList();

    public async Task<ResumedCartDto> GetAsync(int id)
    {
        var cart = await FindAsync(id);

        var lines = new List<ResumedLineDto>(cart.CartItems.Count);
        foreach (var item in cart.CartItems)
        {
            // Tell the till which price each line was sold at, so a resumed cart shows "Wholesale" or "Custom" instead of guessing from the amount.
            var kind = "default";
            int? tierId = null;
            string? label = null;
            if (item.Product is not null && item.UnitPrice != item.Product.SellingPrice)
            {
                var tier = (await _prices.GetTiersAsync(item.ProductId)).FirstOrDefault(t => t.Price == item.UnitPrice);
                if (tier is not null) { kind = "tier"; tierId = tier.Id; label = tier.Name; }
                else kind = "custom";
            }

            lines.Add(new ResumedLineDto
            {
                ProductId = item.ProductId,
                Name = item.Product?.Name ?? string.Empty,
                Quantity = item.Quantity,
                WeightOrVolume = item.WeightOrVolume,
                PriceKind = kind,
                PriceTierId = tierId,
                PriceLabel = label,
                UnitPrice = item.UnitPrice,
                CurrencyId = item.Product?.CurrencyId ?? 0,
            });
        }

        return new ResumedCartDto
        {
            CartId = cart.Id,
            CustomerId = cart.CustomerId,
            Lines = lines,
            DiscountAmount = cart.DiscountAmount,
            DiscountPercentage = cart.DiscountPercentage,
            DiscountLabel = null,
            Notes = NotesOf(cart).Notes,
        };
    }

    public async Task DeleteAsync(int id) => await SoftDeleteAsync((await FindAsync(id)).Id);

    public async Task<int> DeleteAllAsync()
    {
        var held = await HeldCartsAsync();
        foreach (var cart in held) await SoftDeleteAsync(cart.Id);
        return held.Count;
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    /// <summary>Held carts that were not sold: "inactive and not deleted" also matches the carts of finished sales, which have an order.</summary>
    private async Task<List<Cart>> HeldCartsAsync()
    {
        var all = await _carts.GetAllHeldCartsAsync();
        var sold = (await _orders.GetByDateRangeAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow)).Select(o => o.CartId).ToHashSet();
        return all.Where(c => !sold.Contains(c.Id)).ToList();
    }

    private async Task<Cart> FindAsync(int id)
    {
        Cart? cart = null;
        try { cart = await _carts.GetByIdAsync(id); }
        catch (Exception) { /* the repository throws for an unknown id */ }

        if (cart is null || cart.IsDeleted)
            throw new PosRuleException(StatusCodes.Status404NotFound, PosCodes.CartNotFound, "Pos_CartNotFound");
        return cart;
    }

    private async Task SoftDeleteAsync(int id)
    {
        var cart = await _carts.GetByIdAsync(id);
        cart.IsDeleted = true;
        cart.UpdatedAt = DateTime.UtcNow;
        await _cartRepo.SaveAsync(cart);
    }

    private static (bool IsWalkIn, string Notes) NotesOf(Cart cart)
    {
        var raw = cart.DiscountCode ?? string.Empty;
        return raw.StartsWith(WalkInMarker, StringComparison.Ordinal) ? (true, raw[WalkInMarker.Length..]) : (false, raw);
    }

    private static HeldCartDto Map(Cart cart)
    {
        var (walkIn, notes) = NotesOf(cart);
        // A cart whose notes carry no marker but has no customer is a walk-in as well (the old list only used the marker; this is stricter and still safe).
        var isWalkIn = walkIn || cart.Customer is null;
        return new HeldCartDto
        {
            Id = cart.Id,
            CustomerId = cart.CustomerId,
            CustomerName = walkIn || cart.Customer is null ? string.Empty : $"{cart.Customer.FirstName} {cart.Customer.LastName}".Trim(),
            IsWalkIn = isWalkIn,
            TotalAmount = cart.CartItems.Sum(i => i.TotalPrice),
            ItemCount = cart.CartItems.Sum(i => i.Quantity),
            CreatedAt = cart.CreatedAt,
            Notes = notes,
        };
    }
}
