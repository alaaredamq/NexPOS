using Core.IRepositories.PrinterTransport;
using Core.Printing;
using SkiaSharp;

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

        using var stream = new SKDynamicMemoryWStream();
        using (var pdf = SKDocument.CreatePdf(stream))
        {
            for (var copy = 0; copy < label.Copies; copy++)
            {
                using var canvas = pdf.BeginPage(pageWidth, pageHeight);
                canvas.Clear(SKColors.White);
                LabelArtwork.Draw(canvas, label, PointsPerMm, wholeDotBars: false);
                pdf.EndPage();
            }
            pdf.Close();
        }

        using var data = stream.DetachAsData();
        return data.ToArray();
    }
}
