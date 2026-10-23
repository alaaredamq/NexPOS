using Core.IRepositories.PrinterTransport;
using Core.Printing;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Infrastructure.Repositories.PrinterTransport;

/// <summary>
/// Barcode stickers as a PDF - what a plain browser gets instead of a print dialog. Mirrors the
/// layout the desktop app prints (name / barcode / code / price, scaled from the 40x25mm design).
/// </summary>
public class LabelPdfBuilder : ILabelPdfBuilder
{
    private const float PointsPerMm = 72f / 25.4f;

    public byte[] Build(StickerLabel label)
    {
        var pageWidth = label.WidthMm * PointsPerMm;
        var pageHeight = label.HeightMm * PointsPerMm;
        // Every measurement below is the 40x25mm design value scaled to the chosen sticker.
        var scale = Math.Min(label.WidthMm / 40f, label.HeightMm / 25f);
        float Mm(float designMm) => designMm * scale * PointsPerMm;

        using var stream = new SKDynamicMemoryWStream();
        using (var pdf = SKDocument.CreatePdf(stream))
        using (var arabicTypeface = SKFontManager.Default.MatchCharacter(0x0627) ?? SKFontManager.Default.MatchFamily("Tahoma") ?? SKTypeface.Default)
        using (var arabicShaper = new SKShaper(arabicTypeface))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            SKFont Font(string text, float size, bool bold) =>
                new(EscPosBuilder.ContainsArabic(text) ? arabicTypeface : SKTypeface.Default, size) { Embolden = bold };

            void Centered(SKCanvas canvas, string text, float centerX, float baseline, SKFont font)
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
            var barcodeHeight = label.HeightMm * 0.28f * PointsPerMm;
            var barcodeWidth = label.WidthMm * 0.85f * PointsPerMm;
            var skuHeight = skuFont.Size * 1.2f;
            var priceHeight = priceFont.Size * 1.2f;
            var gap = Mm(0.6f);
            var totalHeight = nameHeight + gap + barcodeHeight + gap + skuHeight + (showPrice ? gap + priceHeight : 0);

            for (var copy = 0; copy < label.Copies; copy++)
            {
                using var canvas = pdf.BeginPage(pageWidth, pageHeight);
                canvas.Clear(SKColors.White);

                var y = Math.Max(0, (pageHeight - totalHeight) / 2);
                var centerX = pageWidth / 2;

                Centered(canvas, name, centerX, y + nameFont.Size, nameFont);
                y += nameHeight + gap;

                DrawBars(canvas, label.Bars, (pageWidth - barcodeWidth) / 2, y, barcodeWidth, barcodeHeight, paint);
                y += barcodeHeight + gap;

                Centered(canvas, label.Code, centerX, y + skuFont.Size, skuFont);
                y += skuHeight;

                if (showPrice)
                    Centered(canvas, label.Price, centerX, y + gap + priceFont.Size, priceFont);

                pdf.EndPage();
            }
            pdf.Close();
        }

        using var data = stream.DetachAsData();
        return data.ToArray();
    }

    /// <summary>Draws each run of '1' modules as one rectangle so the bars stay crisp at any zoom.</summary>
    private static void DrawBars(SKCanvas canvas, string bars, float left, float top, float width, float height, SKPaint paint)
    {
        var moduleWidth = width / bars.Length;
        for (var i = 0; i < bars.Length; i++)
        {
            if (bars[i] != '1') continue;

            var start = i;
            while (i + 1 < bars.Length && bars[i + 1] == '1') i++;
            canvas.DrawRect(left + start * moduleWidth, top, (i - start + 1) * moduleWidth, height, paint);
        }
    }
}
