using System.Text;
using Core.IRepositories.PrinterTransport;
using Core.Printing;
using SkiaSharp;

namespace Infrastructure.Repositories.PrinterTransport;

/// <summary>
/// A sticker as TSPL: <c>SIZE</c> / <c>GAP</c> set the label, the artwork goes down as one 1-bit <c>BITMAP</c> (so Arabic
/// names and the exact layout of the PDF work, with no printer fonts involved) and <c>PRINT</c> repeats it per copy.
/// </summary>
public class TsplLabelBuilder : ITsplLabelBuilder
{
    /// <summary>203 dpi, which is what the common 2-4 inch TSPL printers (XP-365B, TSC TTP-244...) have: 8 dots per millimetre.</summary>
    internal const int DotsPerMm = 8;

    /// <summary>The space between labels on the roll. The printer senses the real gap; this only has to be close.</summary>
    private const int GapMm = 2;

    public byte[] Build(StickerLabel label)
    {
        var widthDots = label.WidthMm * DotsPerMm;
        var heightDots = label.HeightMm * DotsPerMm;
        var widthBytes = (widthDots + 7) / 8;

        using var bitmap = new SKBitmap(widthBytes * 8, heightDots, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            LabelArtwork.Draw(canvas, label, DotsPerMm, wholeDotBars: true);
        }

        using var output = new MemoryStream();
        Write(output, $"SIZE {label.WidthMm} mm,{label.HeightMm} mm\r\n");
        Write(output, $"GAP {GapMm} mm,0 mm\r\n");
        Write(output, "DIRECTION 0\r\n");
        Write(output, "REFERENCE 0,0\r\n");
        Write(output, "CLS\r\n");
        Write(output, $"BITMAP 0,0,{widthBytes},{heightDots},0,");
        output.Write(Pack(bitmap, widthBytes));
        Write(output, "\r\n");
        Write(output, $"PRINT 1,{label.Copies}\r\n");
        return output.ToArray();
    }

    /// <summary>
    /// The same sticker for a 2-in-1 printer that is in receipt mode (it ignores TSPL there): the artwork as ESC/POS raster pictures.
    /// From the left edge of the head, where a narrow sticker roll sits against the guide (centring would put a 40 mm label in the
    /// middle of the 80 mm head, half off the sticker). Receipt mode does not look for the gaps between stickers, so each copy advances
    /// exactly one sticker (its height plus the gap): the copies stay in step with the roll, wherever the first one starts.
    /// </summary>
    public byte[] BuildForReceiptMode(StickerLabel label)
    {
        var widthDots = label.WidthMm * DotsPerMm;
        var heightDots = label.HeightMm * DotsPerMm;
        using var bitmap = new SKBitmap((widthDots + 7) / 8 * 8, heightDots, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            LabelArtwork.Draw(canvas, label, DotsPerMm, wholeDotBars: true);
        }

        var output = new List<byte> { 0x1B, 0x40, 0x1B, (byte)'a', 0x00 }; // initialise, from the left edge
        var picture = EscPosBuilder.RasterBands(bitmap);
        for (var copy = 0; copy < label.Copies; copy++)
        {
            output.AddRange(picture);
            output.AddRange(new byte[] { 0x1B, (byte)'J', (byte)(GapMm * DotsPerMm) }); // feed exactly the gap: next copy on the next sticker
        }
        return output.ToArray();
    }

    /// <summary>
    /// A picture as one TSPL job on continuous paper: a receipt for a 2-in-1 printer that is in label mode (it ignores ESC/POS there).
    /// <c>GAP 0</c> tells the printer the paper has no gaps, so the receipt prints as one long strip. <c>DIRECTION 1</c>, not the
    /// stickers' 0: with 0 the picture comes out turned 180° from a receipt printed in receipt mode (harmless on a sticker you peel
    /// and turn, upside down on a slip you tear off and read).
    /// </summary>
    internal static byte[] ContinuousJob(SKBitmap bitmap)
    {
        var widthBytes = (bitmap.Width + 7) / 8;
        // A few millimetres of blank paper at the end, so the last line clears the tear bar.
        var heightMm = (bitmap.Height + DotsPerMm - 1) / DotsPerMm + 8;

        using var output = new MemoryStream();
        Write(output, $"SIZE {widthBytes * 8 / DotsPerMm} mm,{heightMm} mm\r\n");
        Write(output, "GAP 0 mm,0 mm\r\n");
        Write(output, "DIRECTION 1\r\n");
        Write(output, "REFERENCE 0,0\r\n");
        Write(output, "CLS\r\n");
        Write(output, $"BITMAP 0,0,{widthBytes},{bitmap.Height},0,");
        output.Write(Pack(bitmap, widthBytes));
        Write(output, "\r\n");
        Write(output, "PRINT 1,1\r\n");
        return output.ToArray();
    }

    /// <summary>Rows of bytes, most significant bit first, where a dark pixel is a 0 bit (TSPL BITMAP: 0 = black, 1 = white).</summary>
    internal static byte[] Pack(SKBitmap bitmap, int widthBytes)
    {
        var packed = new byte[widthBytes * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var luminance = (pixel.Red * 299 + pixel.Green * 587 + pixel.Blue * 114) / 1000;
                if (luminance >= 128) packed[y * widthBytes + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
        return packed;
    }

    private static void Write(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
