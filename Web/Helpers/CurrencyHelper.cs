namespace Web.Helpers;

/// <summary>
/// Helper class for currency formatting throughout the application.
/// Uses the application's Currency.Notation instead of .NET's culture-based formatting.
/// </summary>
public static class CurrencyHelper
{
    /// <summary>
    /// Formats a decimal value as currency with the specified notation.
    /// </summary>
    /// <param name="amount">The amount to format</param>
    /// <param name="notation">The currency notation (e.g., "$", "LBP", "ل.ل")</param>
    /// <returns>Formatted currency string</returns>
    public static string FormatCurrency(decimal amount, string? notation = null)
    {
        notation = notation ?? "$";
        return $"{notation}{amount:N2}";
    }

    /// <summary>
    /// Formats a nullable decimal value as currency with the specified notation.
    /// </summary>
    /// <param name="amount">The amount to format (nullable)</param>
    /// <param name="notation">The currency notation (e.g., "$", "LBP", "ل.ل")</param>
    /// <returns>Formatted currency string</returns>
    public static string FormatCurrency(decimal? amount, string? notation = null)
    {
        return FormatCurrency(amount ?? 0, notation);
    }

    /// <summary>
    /// Formats a float value as currency with the specified notation.
    /// </summary>
    public static string FormatCurrency(float amount, string? notation = null)
    {
        return FormatCurrency((decimal)amount, notation);
    }

    /// <summary>
    /// Formats a double value as currency with the specified notation.
    /// </summary>
    public static string FormatCurrency(double amount, string? notation = null)
    {
        return FormatCurrency((decimal)amount, notation);
    }
}
