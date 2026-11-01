using Application.IServices.Base;
using Application.IServices.Products;
using Application.Services.Products;
using Core.Discounts;
using Core.Entities.Sales;
using Web.Api.Infrastructure;
using Web.Api.Pos.Contracts;

namespace Web.Api.Pos;

/// <summary>
/// The server's price of a cart: the only place that decides what a till shows and what checkout then accepts.
/// Reads only; the same numbers feed <c>pos/price</c>, a held cart's validation and the checkout.
/// </summary>
public interface IPosPricingService
{
    Task<PosPricedCartDto> PriceAsync(PosCartInput cart);

    /// <summary>The priced cart plus what checkout needs to store it (each line's product currency).</summary>
    Task<PosPricing> PriceDetailedAsync(PosCartInput cart);
}

/// <param name="Cart">What the API returns.</param>
/// <param name="Lines">The same lines, in the same order, with the product's own currency.</param>
public sealed record PosPricing(PosPricedCartDto Cart, IReadOnlyList<PosPricedLine> Lines);

public sealed record PosPricedLine(PosLineInput Input, int ProductCurrencyId, decimal UnitPriceInCartCurrency);

/// <summary>
/// The arithmetic of a till price, kept as pure functions so the C# tests and the browser's Vitest
/// (<c>docs/react-migration/golden/pos-pricing.json</c>) check the very same cases.
/// </summary>
public static class PosPricingMath
{
    /// <summary>A unit price in the product's currency -> the cart's. Same currency: untouched. Otherwise through USD, rounded to 2 decimals
    /// (<c>Math.Round</c>, banker's rounding - what the old <c>ConvertPrice</c> endpoint returned).</summary>
    public static decimal ToCartCurrency(decimal price, Currency productCurrency, Currency cartCurrency)
        => productCurrency.Id == cartCurrency.Id
            ? price
            : Math.Round(PriceRules.ConvertViaUsd(price, productCurrency, cartCurrency), 2);

    /// <summary>Weight/volume sales multiply by the amount, piece sales by the count - exactly <c>CartItem.TotalPrice</c>.</summary>
    public static decimal LineTotal(decimal unitPrice, int quantity, decimal? weightOrVolume)
        => weightOrVolume is > 0 ? unitPrice * weightOrVolume.Value : unitPrice * quantity;

    /// <summary>What the customer pays: subtotal minus the discount, rounded to 2 decimals.</summary>
    public static decimal Total(decimal subtotal, decimal discountAmount) => Math.Round(subtotal - discountAmount, 2);
}

public sealed class PosPricingService : IPosPricingService
{
    private readonly IFastProductSearchService _products;
    private readonly IProductPriceService _prices;
    private readonly ICurrencyService _currencies;

    public PosPricingService(IFastProductSearchService products, IProductPriceService prices, ICurrencyService currencies)
    {
        _products = products;
        _prices = prices;
        _currencies = currencies;
    }

    public async Task<PosPricedCartDto> PriceAsync(PosCartInput cart) => (await PriceDetailedAsync(cart)).Cart;

    public async Task<PosPricing> PriceDetailedAsync(PosCartInput cart)
    {
        if (cart.Lines.Count == 0)
            throw new PosRuleException(StatusCodes.Status400BadRequest, ApiCodes.ValidationFailed, "Pos_CartEmpty", "lines");

        var duplicate = cart.Lines.GroupBy(l => l.ProductId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.DuplicateProduct, "Pos_DuplicateProduct", "lines", duplicate.Key);

        var currencies = await _currencies.GetAllAsync();
        var cartCurrency = currencies.FirstOrDefault(c => c.Id == cart.CurrencyId && c.IsActive)
            ?? throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.CurrencyNotFound, "Pos_CurrencyNotFound", "currencyId");

        var priced = new List<PosPricedLine>(cart.Lines.Count);
        var lines = new List<PosPricedLineDto>(cart.Lines.Count);

        foreach (var input in cart.Lines)
        {
            if (input.WeightOrVolume is <= 0)
                throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.InvalidAmount, "Pos_InvalidAmount", "lines");

            var product = await _products.GetByIdAsync(input.ProductId)
                ?? throw new PosRuleException(StatusCodes.Status404NotFound, PosCodes.ProductNotFound, "Pos_ProductNotFound", null, input.ProductId);

            var productCurrency = currencies.FirstOrDefault(c => c.Id == product.CurrencyId)
                ?? throw new PosRuleException(StatusCodes.Status409Conflict, PosCodes.CurrencyNotFound, "Pos_CurrencyNotFound");

            var minUnit = PriceRules.MinimumUnitPrice(product, productCurrency, cartCurrency);
            decimal unit;
            string? label = null;
            var custom = false;

            if (input.PriceTierId is { } tierId)
            {
                var tier = (await _prices.GetTiersAsync(product.Id)).FirstOrDefault(t => t.Id == tierId)
                    ?? throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.PriceNotAvailable, "Pos_PriceNotAvailable", "lines", product.Name);
                unit = PosPricingMath.ToCartCurrency(tier.Price, productCurrency, cartCurrency);
                label = tier.Name;
            }
            else if (input.CustomPrice is { } typed)
            {
                if (typed <= 0 || typed < minUnit)
                    throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.PriceBelowMinimum, "Pos_PriceBelowMinimum", "lines", product.Name, typed.ToString("N2"), minUnit.ToString("N2"));
                // Typed in the cart currency, so it is the cart's unit price as it stands (the cart stores it in the product's currency, checkout converts back).
                unit = typed;
                custom = true;
            }
            else
            {
                unit = PosPricingMath.ToCartCurrency(product.SellingPrice, productCurrency, cartCurrency);
            }

            var need = input.WeightOrVolume is > 0 ? input.WeightOrVolume.Value : input.Quantity;
            priced.Add(new PosPricedLine(input, product.CurrencyId, unit));
            lines.Add(new PosPricedLineDto
            {
                ProductId = product.Id,
                Name = product.Name,
                Quantity = input.Quantity,
                WeightOrVolume = input.WeightOrVolume,
                PriceTierId = input.PriceTierId,
                PriceLabel = label,
                IsCustomPrice = custom,
                UnitPrice = unit,
                LineTotal = PosPricingMath.LineTotal(unit, input.Quantity, input.WeightOrVolume),
                MinUnitPrice = minUnit,
                StockQuantity = product.StockQuantity,
                InsufficientStock = product.StockQuantity < need,
            });
        }

        var subtotal = lines.Sum(l => l.LineTotal);
        var discount = ResolveDiscount(subtotal, cart);

        var result = new PosPricedCartDto
        {
            CurrencyId = cartCurrency.Id,
            CurrencyNotation = cartCurrency.Notation,
            Lines = lines,
            Subtotal = subtotal,
            DiscountAmount = discount.Amount,
            DiscountPercentage = discount.Percentage,
            TaxAmount = 0m,
            Total = PosPricingMath.Total(subtotal, discount.Amount),
            HasInsufficientStock = lines.Any(l => l.InsufficientStock),
        };
        return new PosPricing(result, priced);
    }

    private static DiscountBreakdown ResolveDiscount(decimal subtotal, PosCartInput cart)
    {
        if (cart.DiscountPercentage is { } pct && (pct < 0 || pct > 100))
            throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.InvalidDiscount, "Pos_InvalidDiscountPercentage", "discountPercentage");
        if (cart.DiscountAmount is < 0)
            throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.InvalidDiscount, "Pos_InvalidDiscountAmount", "discountAmount");

        var breakdown = DiscountBreakdown.From(subtotal, cart.DiscountAmount, cart.DiscountPercentage);
        // A discount larger than the sale would make a negative total, i.e. pay the customer.
        if (breakdown.Amount > subtotal)
            throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.InvalidDiscount, "Pos_DiscountExceedsSubtotal", "discountAmount");
        return breakdown;
    }
}
