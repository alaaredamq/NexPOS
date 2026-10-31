namespace Common.Helpers;

/// <summary>
/// One muted colour per product category, derived from the category's name so it never needs storing.
/// The dashboard charts use <see cref="ColorFor"/> and the POS keys use <see cref="ToneFor"/> (the same
/// index, written to data-tone and mapped to these hues in pos.css), so a category reads as the same
/// colour on both screens. The hues are Boussi Cell's: brand teal first, then blue, amber, green, rose,
/// violet and slate. Red is left out on purpose - it already means "out of stock" / "danger".
/// </summary>
public static class CategoryPalette
{
    public static readonly string[] Colors =
    {
        "#0a7b85", "#2361d8", "#b4610f", "#178a50", "#a8456b", "#5a4ad1", "#5c6472",
    };

    /// <summary>Index into <see cref="Colors"/>. Stable across runs (string.GetHashCode is not).</summary>
    public static int ToneFor(string? categoryName)
    {
        var key = (categoryName ?? string.Empty).Trim().ToLowerInvariant();
        var hash = 17;
        foreach (var ch in key)
            hash = unchecked(hash * 31 + ch);
        return (int)((uint)hash % (uint)Colors.Length);
    }

    public static string ColorFor(string? categoryName) => Colors[ToneFor(categoryName)];
}
