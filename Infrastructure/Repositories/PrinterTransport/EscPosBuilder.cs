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
    private const float MinFontSize = 16f;
    private const float LineSpacing = 1.4f;

    /// <summary>Font A columns on 80mm paper - the text-mode line width.</summary>
    private const int TextColumns = 48;

    /// <summary>Lines fed before the cut so the last printed line clears the cutter.</summary>
    private const byte FeedLinesBeforeCut = 5;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Strict CP437 encoder: throws instead of silently writing '?' for characters the
    // printer's built-in code page can't represent - that's how we decide to rasterize.
    private static readonly Encoding Cp437Strict;

    // .NET Core/5+ only ships Unicode encodings out of the box; code page 437 (used for the
    // plain-text ESC/POS receipt path) needs this provider registered before it can be resolved.
    static EscPosBuilder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp437Strict = Encoding.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    public byte[] BuildReceipt(Receipt receipt, StoreSettings? storeSettings = null)
    {
        var lines = BuildLines(receipt, storeSettings);
        return lines.All(l => CanPrintAsText(l.Text) && CanPrintAsText(l.Right))
            ? EncodeLinesAsText(lines)
            : EncodeLinesAsImage(lines);
    }

    public string BuildPreviewText(Receipt receipt, StoreSettings? storeSettings = null)
    {
        var sb = new StringBuilder();
        foreach (var line in BuildLines(receipt, storeSettings))
        {
            sb.AppendLine(line.Kind switch
            {
                LineKind.Separator => new string('-', TextColumns),
                LineKind.Spacer => "",
                _ when line.Right != null => FormatRow(line.Text, line.Right),
                _ when line.Align == SKTextAlign.Center => line.Text.PadLeft((TextColumns + line.Text.Length) / 2),
                _ => line.Text,
            });
        }
        return sb.ToString();
    }

    public bool RequiresImage(string text) => !CanPrintAsText(text);

    public byte[] BuildTextImage(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(t => new ReceiptLine(t, SKTextAlign.Left, t.Length == 0 ? LineKind.Spacer : LineKind.Normal))
            .ToList();
        return EncodeLinesAsImage(lines);
    }

    private static bool CanPrintAsText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        try
        {
            Cp437Strict.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private enum LineKind { Header, Normal, Bold, Separator, Spacer }

    /// <summary>
    /// One printed line. When <paramref name="Right"/> is set the line is a two-column row:
    /// <paramref name="Text"/> on the left edge, <paramref name="Right"/> on the right edge.
    /// </summary>
    private record ReceiptLine(string Text, SKTextAlign Align, LineKind Kind, string? Right = null);

    /// <summary>
    /// The receipt's content and order, kept in one place so the two rendering paths
    /// (text vs. image) never drift apart.
    /// </summary>
    private static List<ReceiptLine> BuildLines(Receipt receipt, StoreSettings? storeSettings)
    {
        var lines = new List<ReceiptLine>();
        var notation = receipt.Currency?.Notation ?? "";
        string Money(decimal amount) => $"{notation}{amount.ToString("F2", Inv)}";
        void Row(string label, string value, LineKind kind = LineKind.Normal) =>
            lines.Add(new ReceiptLine(label, SKTextAlign.Left, kind, value));
        void Centered(string? text)
        {
            if (!string.IsNullOrEmpty(text))
                lines.Add(new ReceiptLine(text, SKTextAlign.Center, LineKind.Normal));
        }
        void Separator() => lines.Add(new ReceiptLine("", SKTextAlign.Left, LineKind.Separator));

        // Header
        if (storeSettings != null)
        {
            lines.Add(new ReceiptLine(storeSettings.StoreName, SKTextAlign.Center, LineKind.Header));
            Centered(storeSettings.AddressLine1);
            Centered(storeSettings.AddressLine2);
            if (!string.IsNullOrEmpty(storeSettings.Phone))
                Centered($"Phone: {storeSettings.Phone}");
            if (!string.IsNullOrEmpty(storeSettings.TaxId))
                Centered($"Tax ID: {storeSettings.TaxId}");
        }
        else
        {
            lines.Add(new ReceiptLine("RECEIPT", SKTextAlign.Center, LineKind.Header));
        }

        // Receipt meta
        Separator();
        Row("Receipt #:", receipt.ReceiptNumber);
        Row("Date:", receipt.ReceiptDate.ToString("yyyy-MM-dd HH:mm", Inv));

        var customerName = !string.IsNullOrWhiteSpace(receipt.CustomerName)
            ? receipt.CustomerName.Trim()
            : $"{receipt.Customer?.FirstName} {receipt.Customer?.LastName}".Trim();
        if (!string.IsNullOrEmpty(customerName))
            Row("Customer:", customerName);

        if (receipt.Cashier != null)
        {
            var cashierName = $"{receipt.Cashier.FirstName} {receipt.Cashier.LastName}".Trim();
            Row("Cashier:", string.IsNullOrEmpty(cashierName) ? receipt.Cashier.Username : cashierName);
        }

        // Items
        Separator();
        foreach (var item in receipt.ReceiptItems)
        {
            lines.Add(new ReceiptLine(item.ProductName, SKTextAlign.Left, LineKind.Bold));

            var quantityDisplay = item.Quantity.ToString("0.###", Inv);
            if (!string.IsNullOrEmpty(item.Unit) && !item.Unit.Equals("piece", StringComparison.OrdinalIgnoreCase))
                quantityDisplay += $" {item.Unit}";
            Row($"  {quantityDisplay} x {Money(item.UnitPrice)}", Money(item.LineTotal));

            if (item.DiscountAmount > 0)
                Row("  Discount", $"-{Money(item.DiscountAmount)}");
        }

        // Totals
        Separator();
        Row("Subtotal:", Money(receipt.SubTotal));
        if (receipt.DiscountAmount > 0)
            Row("Discount:", $"-{Money(receipt.DiscountAmount)}");
        if (receipt.TaxAmount > 0)
            Row("Tax:", Money(receipt.TaxAmount));
        Row("TOTAL:", Money(receipt.TotalAmount), LineKind.Bold);

        // Payment
        Separator();
        if (!string.IsNullOrEmpty(receipt.PaymentMethod?.Name))
            Row("Payment:", receipt.PaymentMethod.Name);
        if (receipt.AmountPaid > 0)
            Row("Paid:", Money(receipt.AmountPaid));
        if (receipt.ChangeAmount > 0)
            Row("Change:", Money(receipt.ChangeAmount));

        // Footer
        lines.Add(new ReceiptLine("", SKTextAlign.Left, LineKind.Spacer));
        if (storeSettings != null)
        {
            Centered(storeSettings.ReceiptFooterMessage);
            Centered(storeSettings.FooterLine1);
            Centered(storeSettings.FooterLine2);
            Centered(storeSettings.FooterLine3);
        }
        else
        {
            Centered("Thank you!");
        }

        return lines;
    }

    private static string FormatRow(string left, string right)
    {
        var spaces = Math.Max(1, TextColumns - left.Length - right.Length);
        return left + new string(' ', spaces) + right;
    }

    private static byte[] EncodeLinesAsText(List<ReceiptLine> lines)
    {
        var sb = new StringBuilder();
        const string ESC = "\x1B";
        const string GS = "\x1D";

        sb.Append(ESC + "@");
        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case LineKind.Separator:
                    sb.Append(ESC + "a\x00");
                    sb.AppendLine(new string('-', TextColumns));
                    continue;
                case LineKind.Spacer:
                    sb.AppendLine();
                    continue;
                case LineKind.Header:
                    sb.Append(ESC + "a\x01");
                    sb.Append(GS + "!\x11"); // Double height and width
                    sb.AppendLine(line.Text);
                    sb.Append(GS + "!\x00");
                    continue;
            }

            sb.Append(ESC + "a" + (line.Right == null && line.Align == SKTextAlign.Center ? "\x01" : "\x00"));
            if (line.Kind == LineKind.Bold) sb.Append(ESC + "E\x01");
            sb.AppendLine(line.Right != null ? FormatRow(line.Text, line.Right) : line.Text);
            if (line.Kind == LineKind.Bold) sb.Append(ESC + "E\x00");
        }

        sb.Append(ESC + "a\x00");
        sb.Append(ESC + "d" + (char)FeedLinesBeforeCut);
        sb.Append(GS + "V\x01"); // Cut paper

        return Cp437Strict.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Arabic (incl. presentation forms) - needs HarfBuzz shaping and right-to-left layout
    /// when drawn into the receipt image.
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

    private static byte[] EncodeLinesAsImage(List<ReceiptLine> lines)
    {
        using var bitmap = RenderBitmap(lines);
        return EncodeRaster(bitmap);
    }

    private static SKBitmap RenderBitmap(List<ReceiptLine> lines)
    {
        using var latinTypeface = SKTypeface.Default;
        // MatchCharacter asks the OS font system for whichever installed font actually has
        // Arabic glyphs (varies by machine/Windows language pack) rather than betting on one
        // hardcoded font name being present.
        using var arabicTypeface = SKFontManager.Default.MatchCharacter(0x0627)
            ?? SKFontManager.Default.MatchFamily("Tahoma")
            ?? SKTypeface.Default;
        using var arabicShaper = new SKShaper(arabicTypeface);

        // Height is computed analytically (font size * fixed spacing per line) rather than by
        // an actual measure pass - lines that are too wide shrink their font instead of
        // wrapping, so every line keeps its fixed height and this stays a single drawing pass.
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
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        const float usableWidth = PaperWidthDots - Margin * 2;
        const float columnGap = 12f;

        SKFont MakeFont(string text, float size, bool bold) =>
            new(ContainsArabic(text) ? arabicTypeface : latinTypeface, size) { Embolden = bold };

        void Draw(string text, float x, float y, SKTextAlign align, SKFont font)
        {
            if (ContainsArabic(text))
                canvas.DrawShapedText(arabicShaper, text, x, y, align, font, paint);
            else
                canvas.DrawText(text, x, y, align, font, paint);
        }

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

            var lineBoxSize = line.Kind == LineKind.Header ? HeaderFontSize : NormalFontSize;
            var bold = line.Kind == LineKind.Bold;

            using var leftFont = MakeFont(line.Text, lineBoxSize, bold);
            using var rightFont = MakeFont(line.Right ?? "", lineBoxSize, bold);

            // Shrink (both columns together) until the line fits the paper, rather than
            // letting a long product name run off the edge.
            float Needed() => leftFont.MeasureText(line.Text) +
                (line.Right != null ? columnGap + rightFont.MeasureText(line.Right) : 0);
            while (Needed() > usableWidth && leftFont.Size > MinFontSize)
            {
                leftFont.Size -= 1;
                rightFont.Size -= 1;
            }

            y += lineBoxSize; // baseline sits at the bottom of this line's box

            if (line.Right != null)
            {
                Draw(line.Text, Margin, y, SKTextAlign.Left, leftFont);
                Draw(line.Right, PaperWidthDots - Margin, y, SKTextAlign.Right, rightFont);
            }
            else
            {
                // Arabic text is right-aligned regardless of the line's nominal alignment - a
                // left-aligned "field" of Arabic text should still start from the paper's right
                // edge and read right-to-left, matching how a receipt in an RTL language is
                // actually laid out rather than how the (LTR-authored) line list positions it.
                var align = ContainsArabic(line.Text) && line.Align == SKTextAlign.Left ? SKTextAlign.Right : line.Align;
                var x = align switch
                {
                    SKTextAlign.Center => PaperWidthDots / 2f,
                    SKTextAlign.Right => PaperWidthDots - Margin,
                    _ => Margin,
                };
                Draw(line.Text, x, y, align, leftFont);
            }

            y += lineBoxSize * (LineSpacing - 1f);
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
        result.AddRange(new byte[] { 0x1B, (byte)'d', FeedLinesBeforeCut }); // Feed past the cutter
        result.AddRange(new byte[] { 0x1D, (byte)'V', 0x01 }); // Cut paper

        return result.ToArray();
    }
}
