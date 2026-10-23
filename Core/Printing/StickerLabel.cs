namespace Core.Printing;

/// <summary>
/// One barcode sticker. <see cref="Bars"/> is the barcode as modules ('1' = bar, '0' = space),
/// already encoded by the browser (JsBarcode), so the server only has to draw it.
/// </summary>
public record StickerLabel(string Name, string Code, string Price, string Bars, int WidthMm, int HeightMm, int Copies, bool ShowPrice)
{
    public const int MaxCopies = 100;
    public const int MaxBarModules = 2000;
    private const int MinWidthMm = 15, MaxWidthMm = 150, MinHeightMm = 10, MaxHeightMm = 150;

    /// <summary>Clamps sizes and counts (the same limits the browser dialog applies) and rejects unusable bars.</summary>
    public static StickerLabel? TryCreate(string? name, string? code, string? price, string? bars, int widthMm, int heightMm, int copies, bool showPrice)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrEmpty(bars)
            || bars.Length > MaxBarModules || !bars.All(c => c is '0' or '1') || !bars.Contains('1'))
            return null;

        return new StickerLabel(
            Truncate(name, 200), Truncate(code.Trim(), 100), Truncate(price, 50), bars,
            Math.Clamp(widthMm, MinWidthMm, MaxWidthMm), Math.Clamp(heightMm, MinHeightMm, MaxHeightMm),
            Math.Clamp(copies, 1, MaxCopies), showPrice);
    }

    private static string Truncate(string? value, int max)
    {
        value = (value ?? "").Trim();
        return value.Length <= max ? value : value[..max];
    }
}
