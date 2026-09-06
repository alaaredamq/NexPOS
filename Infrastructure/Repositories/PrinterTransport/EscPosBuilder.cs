using System.Globalization;
using System.Text;
using Core.Entities.Configuration;
using Core.Entities.Sales;
using Core.IRepositories.PrinterTransport;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Infrastructure.Repositories.PrinterTransport;

public class EscPosBuilder : IEscPosBuilder
{
    private const int PaperWidthDots = 576;
    private const float Margin = 16f;
    private const float NormalFontSize = 26f;
    private const float HeaderFontSize = 40f;
    private const float LineSpacing = 1.4f;

    public byte[] BuildReceipt(Receipt receipt, StoreSettings? storeSettings = null)
    {
        if (ReceiptContainsArabic(receipt, storeSettings))
        {
            return BuildReceiptImage(receipt, storeSettings);
        }

        return BuildReceiptText(receipt, storeSettings);
    }

    private static bool ReceiptContainsArabic(Receipt receipt, StoreSettings? storeSettings)
    {
        if (storeSettings != null &&
            (ContainsArabic(storeSettings.StoreName) ||
             ContainsArabic(storeSettings.AddressLine1) ||
             ContainsArabic(storeSettings.AddressLine2) ||
             ContainsArabic(storeSettings.ReceiptFooterMessage) ||
             ContainsArabic(storeSettings.FooterLine1) ||
             ContainsArabic(storeSettings.FooterLine2)))
        {
            return true;
        }

        return receipt.ReceiptItems.Any(i => ContainsArabic(i.ProductName));
    }

    private byte[] BuildReceiptText(Receipt receipt, StoreSettings? storeSettings)
    {
        var sb = new StringBuilder();
        const string ESC = "\x1B";
        const string GS = "\x1D";

        sb.Append(ESC + "@");
        sb.Append(ESC + "a\x01"); // Center align
        sb.Append(GS + "!\x11"); // Double height and width

        // Use store settings for header if available
        if (storeSettings != null)
        {
            sb.AppendLine(storeSettings.StoreName);
            sb.Append(GS + "!\x00"); // Normal size
            if (!string.IsNullOrEmpty(storeSettings.AddressLine1))
                sb.AppendLine(storeSettings.AddressLine1);
            if (!string.IsNullOrEmpty(storeSettings.AddressLine2))
                sb.AppendLine(storeSettings.AddressLine2);
            if (!string.IsNullOrEmpty(storeSettings.Phone))
                sb.AppendLine($"Phone: {storeSettings.Phone}");
            if (!string.IsNullOrEmpty(storeSettings.TaxId))
                sb.AppendLine($"Tax ID: {storeSettings.TaxId}");
        }
        else
        {
            sb.AppendLine("RECEIPT");
            sb.Append(GS + "!\x00"); // Normal size
        }

        sb.AppendLine("--------------------------------");
        sb.Append(ESC + "a\x00"); // Left align

        sb.AppendLine($"Receipt #: {receipt.ReceiptNumber}");
        sb.AppendLine($"Date: {receipt.ReceiptDate:yyyy-MM-dd HH:mm}");

        sb.AppendLine("--------------------------------");

        foreach (var item in receipt.ReceiptItems)
        {
            sb.AppendLine(item.ProductName);
            var quantityDisplay = item.Quantity.ToString("F3");
            if (!string.IsNullOrEmpty(item.Unit) && item.Unit != "piece")
            {
                quantityDisplay += $" {item.Unit}";
            }
            sb.AppendLine($"  {quantityDisplay} x {item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        sb.AppendLine("--------------------------------");
        sb.Append(ESC + "E\x01"); // Bold on
        sb.AppendLine($"TOTAL: {receipt.TotalAmount.ToString("F2", CultureInfo.InvariantCulture)}");
        sb.Append(ESC + "E\x00"); // Bold off

        sb.AppendLine();
        sb.Append(ESC + "a\x01"); // Center align

        // Use store settings for footer if available
        if (storeSettings != null)
        {
            sb.AppendLine(storeSettings.ReceiptFooterMessage);
            if (!string.IsNullOrEmpty(storeSettings.FooterLine1))
                sb.AppendLine(storeSettings.FooterLine1);
            if (!string.IsNullOrEmpty(storeSettings.FooterLine2))
                sb.AppendLine(storeSettings.FooterLine2);
        }
        else
        {
            sb.AppendLine("Thank you!");
        }

        sb.Append(GS + "V\x01"); // Cut paper

        return Encoding.GetEncoding(437).GetBytes(sb.ToString());
    }

    private enum LineKind { Header, Normal, Bold, Separator, Spacer }

    private record ReceiptLine(string Text, SKTextAlign Align, LineKind Kind);

    /// <summary>
    /// Same content and order as <see cref="BuildReceipt"/>, kept in one place so the two
    /// rendering paths (text vs. image) never drift apart.
    /// </summary>
    private static List<ReceiptLine> BuildLines(Receipt receipt, StoreSettings? storeSettings)
    {
        var lines = new List<ReceiptLine>();

        if (storeSettings != null)
        {
            lines.Add(new ReceiptLine(storeSettings.StoreName, SKTextAlign.Center, LineKind.Header));
            if (!string.IsNullOrEmpty(storeSettings.AddressLine1))
                lines.Add(new ReceiptLine(storeSettings.AddressLine1, SKTextAlign.Center, LineKind.Normal));
            if (!string.IsNullOrEmpty(storeSettings.AddressLine2))
                lines.Add(new ReceiptLine(storeSettings.AddressLine2, SKTextAlign.Center, LineKind.Normal));
            if (!string.IsNullOrEmpty(storeSettings.Phone))
                lines.Add(new ReceiptLine($"Phone: {storeSettings.Phone}", SKTextAlign.Center, LineKind.Normal));
            if (!string.IsNullOrEmpty(storeSettings.TaxId))
                lines.Add(new ReceiptLine($"Tax ID: {storeSettings.TaxId}", SKTextAlign.Center, LineKind.Normal));
        }
        else
        {
            lines.Add(new ReceiptLine("RECEIPT", SKTextAlign.Center, LineKind.Header));
        }

        lines.Add(new ReceiptLine("", SKTextAlign.Left, LineKind.Separator));
        lines.Add(new ReceiptLine($"Receipt #: {receipt.ReceiptNumber}", SKTextAlign.Left, LineKind.Normal));
        lines.Add(new ReceiptLine($"Date: {receipt.ReceiptDate:yyyy-MM-dd HH:mm}", SKTextAlign.Left, LineKind.Normal));
        lines.Add(new ReceiptLine("", SKTextAlign.Left, LineKind.Separator));

        foreach (var item in receipt.ReceiptItems)
        {
            lines.Add(new ReceiptLine(item.ProductName, SKTextAlign.Left, LineKind.Normal));
            var quantityDisplay = item.Quantity.ToString("F3");
            if (!string.IsNullOrEmpty(item.Unit) && item.Unit != "piece")
            {
                quantityDisplay += $" {item.Unit}";
            }
            lines.Add(new ReceiptLine($"  {quantityDisplay} x {item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)}", SKTextAlign.Left, LineKind.Normal));
        }

        lines.Add(new ReceiptLine("", SKTextAlign.Left, LineKind.Separator));
        lines.Add(new ReceiptLine($"TOTAL: {receipt.TotalAmount.ToString("F2", CultureInfo.InvariantCulture)}", SKTextAlign.Left, LineKind.Bold));
        lines.Add(new ReceiptLine("", SKTextAlign.Left, LineKind.Spacer));

        if (storeSettings != null)
        {
            if (!string.IsNullOrEmpty(storeSettings.ReceiptFooterMessage))
                lines.Add(new ReceiptLine(storeSettings.ReceiptFooterMessage, SKTextAlign.Center, LineKind.Normal));
            if (!string.IsNullOrEmpty(storeSettings.FooterLine1))
                lines.Add(new ReceiptLine(storeSettings.FooterLine1, SKTextAlign.Center, LineKind.Normal));
            if (!string.IsNullOrEmpty(storeSettings.FooterLine2))
                lines.Add(new ReceiptLine(storeSettings.FooterLine2, SKTextAlign.Center, LineKind.Normal));
        }
        else
        {
            lines.Add(new ReceiptLine("Thank you!", SKTextAlign.Center, LineKind.Normal));
        }

        return lines;
    }

    /// <summary>
    /// Arabic (incl. presentation forms) - the ranges ESC/POS codepage 437 cannot represent
    /// at all, and the reason this receipt needs to be rasterized instead of sent as text.
    /// </summary>
    private static bool ContainsArabic(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        foreach (var c in text)
        {
            if ((c >= 0x0600 && c <= 0x06FF) ||
                (c >= 0x0750 && c <= 0x077F) ||
                (c >= 0xFB50 && c <= 0xFDFF) ||
                (c >= 0xFE70 && c <= 0xFEFF))
            {
                return true;
            }
        }
        return false;
    }

    private byte[] BuildReceiptImage(Receipt receipt, StoreSettings? storeSettings)
    {
        using var bitmap = RenderBitmap(receipt, storeSettings);
        return EncodeRaster(bitmap);
    }

    private SKBitmap RenderBitmap(Receipt receipt, StoreSettings? storeSettings)
    {
        var lines = BuildLines(receipt, storeSettings);

        using var latinTypeface = SKTypeface.Default;
        // MatchCharacter asks the OS font system for whichever installed font actually has
        // Arabic glyphs (varies by machine/Windows language pack) rather than betting on one
        // hardcoded font name being present.
        using var arabicTypeface = SKFontManager.Default.MatchCharacter(0x0627)
            ?? SKFontManager.Default.MatchFamily("Tahoma")
            ?? SKTypeface.Default;
        using var arabicShaper = new SKShaper(arabicTypeface);

        // Height is computed analytically (font size * fixed spacing per line) rather than by
        // an actual measure pass - matches how the text version has no real line-wrapping
        // either, and keeps this a single drawing pass.
        float height = Margin * 2;
        foreach (var line in lines)
        {
            height += line.Kind switch
            {
                LineKind.Header => HeaderFontSize * LineSpacing,
                LineKind.Separator => NormalFontSize * 0.7f,
                LineKind.Spacer => NormalFontSize * 0.6f,
                _ => NormalFontSize * LineSpacing,
            };
        }

        using var surface = SKSurface.Create(new SKImageInfo(PaperWidthDots, (int)Math.Ceiling(height), SKColorType.Rgba8888, SKAlphaType.Opaque));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var linePaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 2, IsAntialias = true };

        float y = Margin;
        foreach (var line in lines)
        {
            if (line.Kind == LineKind.Separator)
            {
                y += NormalFontSize * 0.35f;
                canvas.DrawLine(Margin, y, PaperWidthDots - Margin, y, linePaint);
                y += NormalFontSize * 0.35f;
                continue;
            }

            if (line.Kind == LineKind.Spacer)
            {
                y += NormalFontSize * 0.6f;
                continue;
            }

            var fontSize = line.Kind == LineKind.Header ? HeaderFontSize : NormalFontSize;
            var isArabic = ContainsArabic(line.Text);
            using var font = new SKFont(isArabic ? arabicTypeface : latinTypeface, fontSize)
            {
                Embolden = line.Kind == LineKind.Bold,
            };
            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true,
            };

            var x = line.Align switch
            {
                SKTextAlign.Center => PaperWidthDots / 2f,
                SKTextAlign.Right => PaperWidthDots - Margin,
                _ => Margin,
            };

            // Arabic text is right-aligned regardless of the line's nominal alignment - a
            // left-aligned "field" of Arabic text should still start from the paper's right
            // edge and read right-to-left, matching how a receipt in an RTL language is
            // actually laid out rather than how the (LTR-authored) line list positions it.
            var effectiveAlign = isArabic && line.Align == SKTextAlign.Left ? SKTextAlign.Right : line.Align;
            var effectiveX = isArabic && line.Align == SKTextAlign.Left ? PaperWidthDots - Margin : x;

            y += fontSize; // baseline sits at the bottom of this line's box

            if (isArabic)
            {
                canvas.DrawShapedText(arabicShaper, line.Text, effectiveX, y, effectiveAlign, font, paint);
            }
            else
            {
                canvas.DrawText(line.Text, effectiveX, y, effectiveAlign, font, paint);
            }

            y += fontSize * (LineSpacing - 1f);
        }

        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    /// <summary>
    /// Encodes a bitmap as an ESC/POS "GS v 0" raster print job - the modern, simpler raster
    /// command supported by virtually all ESC/POS thermal printers (replaces the old strip-based
    /// "ESC *" bit-image mode this file used to build, one command for the whole image).
    /// </summary>
    private static byte[] EncodeRaster(SKBitmap bitmap)
    {
        var widthBytes = (bitmap.Width + 7) / 8;
        var data = new byte[widthBytes * bitmap.Height];

        for (int py = 0; py < bitmap.Height; py++)
        {
            for (int px = 0; px < bitmap.Width; px++)
            {
                var pixel = bitmap.GetPixel(px, py);
                var luminance = (pixel.Red * 299 + pixel.Green * 587 + pixel.Blue * 114) / 1000;
                if (luminance < 160)
                {
                    data[py * widthBytes + (px >> 3)] |= (byte)(0x80 >> (px & 7));
                }
            }
        }

        var result = new List<byte>(data.Length + 16);
        result.AddRange(new byte[] { 0x1B, 0x40 }); // Initialize printer
        result.AddRange(new byte[]
        {
            0x1D, (byte)'v', (byte)'0', 0, // GS v 0, normal mode
            (byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF),
            (byte)(bitmap.Height & 0xFF), (byte)((bitmap.Height >> 8) & 0xFF),
        });
        result.AddRange(data);
        result.AddRange(new byte[] { 0x1D, (byte)'V', 0x01 }); // Cut paper

        return result.ToArray();
    }
}
