using System.Globalization;

namespace Core.Discounts;

/// <summary>
/// A discount expressed both ways - money off and a percentage of the subtotal - no matter which way the
/// cashier entered it. A cart stores only what was typed (<c>DiscountAmount</c> OR <c>DiscountPercentage</c>),
/// so anything that shows or totals a discount (receipts, order pages, the Z-report) goes through here instead
/// of reading <c>DiscountAmount</c> directly, which is null for every percentage discount.
/// </summary>
public readonly record struct DiscountBreakdown(decimal Amount, decimal Percentage)
{
    public static readonly DiscountBreakdown None = new(0m, 0m);

    public bool HasDiscount => Amount > 0;

    /// <summary>The percentage as it should be printed: "10%", "12.5%", "15.15%".</summary>
    public string PercentageText => FormatPercentage(Percentage);

    /// <summary>
    /// A percentage takes precedence over a flat amount when both are set, matching how checkout and
    /// CartSessionService total the cart.
    /// </summary>
    public static DiscountBreakdown From(decimal subtotal, decimal? amount, decimal? percentage)
    {
        if (percentage is > 0)
            return new DiscountBreakdown(subtotal * percentage.Value / 100m, percentage.Value);

        if (amount is > 0)
            return new DiscountBreakdown(amount.Value, subtotal > 0 ? amount.Value / subtotal * 100m : 0m);

        return None;
    }

    /// <summary>At most two decimals, none when the value is whole.</summary>
    public static string FormatPercentage(decimal percentage) =>
        percentage.ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
