using Core.Printing;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Infrastructure.Repositories.PrinterTransport;

/// <summary>
/// Draws one barcode sticker (name / barcode / code / price) onto a canvas. Shared by the PDF the browser downloads
/// and the bitmap sent straight to a label printer, so both read the same. Every measurement is the 40x25mm design
/// value scaled to the chosen sticker; <c>unitsPerMm</c> says what one millimetre is on the canvas (points for a PDF,
/// printer dots for a bitmap).
/// </summary>
internal static class LabelArtwork
{
    public static void Draw(SKCanvas canvas, StickerLabel label, float unitsPerMm, bool wholeDotBars)
    {
        var pageWidth = label.WidthMm * unitsPerMm;
        var pageHeight = label.HeightMm * unitsPerMm;
        var scale = Math.Min(label.WidthMm / 40f, label.HeightMm / 25f);
        float Mm(float designMm) => designMm * scale * unitsPerMm;

        using var arabicTypeface = SKFontManager.Default.MatchCharacter(0x0627) ?? SKFontManager.Default.MatchFamily("Tahoma") ?? SKTypeface.Default;
        using var arabicShaper = new SKShaper(arabicTypeface);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        SKFont Font(string text, float size, bool bold) =>
            new(EscPosBuilder.ContainsArabic(text) ? arabicTypeface : SKTypeface.Default, size) { Embolden = bold };

        void Centered(string text, float centerX, float baseline, SKFont font)
        {
            if (EscPosBuilder.ContainsArabic(text))
                canvas.DrawShapedText(arabicShaper, text, centerX, baseline, SKTextAlign.Center, font, paint);
            else
                canvas.DrawText(text, centerX, baseline, SKTextAlign.Center, font, paint);
        }

        // Names too long for the label are cut with an ellipsis, as the desktop label does.
        var maxTextWidth = pageWidth - Mm(1.6f) * 2;
        var name = label.Name;
        using var nameFont = Font(name, Mm(2.7f), bold: true);
        while (name.Length > 1 && nameFont.MeasureText(name) > maxTextWidth)
            name = name[..^1];
        if (name.Length < label.Name.Length)
            name = name.TrimEnd() + "…";

        var showPrice = label.ShowPrice && label.Price.Length > 0;
        using var skuFont = Font(label.Code, Mm(1.7f), bold: false);
        using var priceFont = Font(label.Price, Mm(4f), bold: true);

        var nameHeight = nameFont.Size * 1.1f;
        var barcodeHeight = label.HeightMm * 0.28f * unitsPerMm;
        var barcodeWidth = label.WidthMm * 0.85f * unitsPerMm;
        var skuHeight = skuFont.Size * 1.2f;
        var priceHeight = priceFont.Size * 1.2f;
        var gap = Mm(0.6f);
        var totalHeight = nameHeight + gap + barcodeHeight + gap + skuHeight + (showPrice ? gap + priceHeight : 0);

        var y = Math.Max(0, (pageHeight - totalHeight) / 2);
        var centerX = pageWidth / 2;

        Centered(name, centerX, y + nameFont.Size, nameFont);
        y += nameHeight + gap;

        DrawBars(canvas, label.Bars, (pageWidth - barcodeWidth) / 2, y, barcodeWidth, barcodeHeight, paint, wholeDotBars);
        y += barcodeHeight + gap;

        Centered(label.Code, centerX, y + skuFont.Size, skuFont);
        y += skuHeight;

        if (showPrice)
            Centered(label.Price, centerX, y + gap + priceFont.Size, priceFont);
    }

    /// <summary>
    /// Draws each run of '1' modules as one rectangle so the bars stay crisp at any zoom. With <paramref name="wholeDotBars"/>
    /// (a printer bitmap) every module is a whole number of dots wide, centred in the space, so no bar comes out thicker
    /// than its neighbour and the barcode keeps scanning.
    /// </summary>
    private static void DrawBars(SKCanvas canvas, string bars, float left, float top, float width, float height, SKPaint paint, bool wholeDotBars)
    {
        var moduleWidth = width / bars.Length;
        if (wholeDotBars && moduleWidth >= 1f)
        {
            moduleWidth = MathF.Floor(moduleWidth);
            left += MathF.Floor((width - moduleWidth * bars.Length) / 2);
        }
        else
        {
            wholeDotBars = false;
        }

        paint.IsAntialias = !wholeDotBars;
        for (var i = 0; i < bars.Length; i++)
        {
            if (bars[i] != '1') continue;

            var start = i;
            while (i + 1 < bars.Length && bars[i + 1] == '1') i++;
            canvas.DrawRect(left + start * moduleWidth, top, (i - start + 1) * moduleWidth, height, paint);
        }
        paint.IsAntialias = true;
    }
}
