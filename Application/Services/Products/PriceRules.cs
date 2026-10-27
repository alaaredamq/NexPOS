using Core.Entities.Products;
using Core.Entities.Sales;

namespace Application.Services.Products;

/// <summary>
/// The one place that decides what a custom price is allowed to be, so the till's price picker
/// and the server-side cart check can never disagree. Currency conversion goes through USD, the
/// same way the POS <c>ConvertPrice</c> endpoint does, so a figure the cashier sees is the figure
/// the server compares against.
/// </summary>
public static class PriceRules
{
    public static decimal ConvertViaUsd(decimal amount, Currency from, Currency to)
    {
        if (from.Id == to.Id) return amount;
        return amount / (decimal)from.RateToUSD * (decimal)to.RateToUSD;
    }

    /// <summary>Rounds up to 2 decimals so a displayed minimum is never lower than the real one.</summary>
    public static decimal CeilToCents(decimal value) => Math.Ceiling(value * 100m) / 100m;

    /// <summary>The lowest custom unit price allowed, expressed in the currency the cart is showing.</summary>
    public static decimal MinimumUnitPrice(Product product, Currency productCurrency, Currency displayCurrency)
        => CeilToCents(ConvertViaUsd(product.CostPrice ?? 0m, productCurrency, displayCurrency));
}
