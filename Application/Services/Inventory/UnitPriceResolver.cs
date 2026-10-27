using Application.IServices.Inventory;
using Application.Services.Products;
using Core.Entities.Products;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Inventory;

/// <summary>
/// Works out the unit price a cart line is charged at. The caller never supplies a price (S2):
/// it names one of the product's stored prices, or asks for a custom price that is checked here
/// against the purchase price. With neither, the product's own selling price applies.
/// The result is in the product's currency, like <see cref="Product.SellingPrice"/>.
/// </summary>
internal static class UnitPriceResolver
{
    public static async Task<decimal> ResolveAsync(ErpDbContext db, Product product, CartItemInput input)
    {
        if (input.PriceTierId.HasValue)
        {
            var tierId = input.PriceTierId.Value;
            var tier = await db.ProductPrices.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tierId && t.ProductId == product.Id);
            if (tier == null)
                throw new ArgumentException($"The selected price is not available for {product.Name}.");
            return tier.Price;
        }

        if (input.CustomUnitPrice.HasValue)
        {
            var custom = input.CustomUnitPrice.Value;
            if (custom <= 0)
                throw new ArgumentException($"The price for {product.Name} must be greater than zero.");

            // The cashier typed the price in the currency the cart shows; the cart stores it in the product's currency.
            var displayCurrencyId = input.CustomPriceCurrencyId ?? product.CurrencyId;
            decimal minimum;
            decimal inProductCurrency = custom;

            if (displayCurrencyId == product.CurrencyId)
            {
                minimum = PriceRules.CeilToCents(product.CostPrice ?? 0m);
            }
            else
            {
                var from = await db.Currencies.AsNoTracking().FirstAsync(c => c.Id == product.CurrencyId);
                var to = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == displayCurrencyId)
                    ?? throw new ArgumentException("The price currency was not found.");
                minimum = PriceRules.MinimumUnitPrice(product, from, to);
                inProductCurrency = PriceRules.ConvertViaUsd(custom, to, from);
            }

            if (custom < minimum)
                throw new ArgumentException($"The price {custom:N2} for {product.Name} is below the minimum of {minimum:N2}.");

            return Math.Round(inProductCurrency, 4);
        }

        return product.SellingPrice;
    }
}
