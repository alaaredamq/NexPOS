using System.Globalization;
using System.Text;
using Core.Entities.Configuration;
using Core.Entities.Sales;
using Core.IRepositories.PrinterTransport;
using Core.Printing;
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

    /// <summary>
    /// Rows per raster command. A long report as one image would overflow most printers'
    /// receive buffer, so it is sent as consecutive bands that print seamlessly. A normal
    /// receipt is shorter than one band and goes out as a single command.
    /// </summary>
    private const int RasterBandRows = 1024;

    /// <summary>PDF points per printer dot: the 576-dot (72mm) print area at the printer's 203 dpi.</summary>
    private const float ReceiptPdfScale = 72f / 203f;
    private const float A4WidthPoints = 595.28f;
    private const float A4HeightPoints = 841.89f;

    /// <summary>
    /// A4 pages show the receipt-width layout smaller (so a page holds a useful number of rows,
    /// centred with a page margin) instead of blown up to the full sheet width.
    /// </summary>
    private const float A4Scale = 0.62f;
    private const float A4PageMarginPoints = 36f;

    /// <summary>PDF viewers refuse pages taller than 200 inches; stay well inside that.</summary>
    private const float MaxReceiptPageHeightPoints = 10000f;

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
        => BuildPrintJob(BuildReceiptDocument(receipt, storeSettings));

    public PrintDocument BuildReceiptDocument(Receipt receipt, StoreSettings? storeSettings = null)
        => new($"Receipt-{receipt.ReceiptNumber}", BuildLines(receipt, storeSettings));

    public byte[] BuildPrintJob(PrintDocument document)
    {
        var lines = document.Lines;
        return lines.All(l => CanPrintAsText(l.Text) && CanPrintAsText(l.Right))
            ? EncodeLinesAsText(lines)
            : EncodeLinesAsImage(lines);
    }

    public byte[] BuildPdf(PrintDocument document, PdfPaper paper = PdfPaper.Receipt)
    {
        var scale = paper == PdfPaper.A4 ? A4Scale : ReceiptPdfScale;
        var maxPageDots = (paper == PdfPaper.A4 ? A4HeightPoints - A4PageMarginPoints * 2 : MaxReceiptPageHeightPoints) / scale;
        var offsetX = paper == PdfPaper.A4 ? (A4WidthPoints - PaperWidthDots * scale) / 2 : 0f;
        var offsetY = paper == PdfPaper.A4 ? A4PageMarginPoints : 0f;

        // Split into pages so no page outgrows maxPageDots. A receipt is one page; a long
        // report flows onto as many A4 pages as it needs.
        var pages = new List<List<PrintLine>> { new() };
        var used = Margin * 2;
        foreach (var line in document.Lines)
        {
            var height = LineHeight(line);
            if (used + height > maxPageDots && pages[^1].Count > 0)
            {
                pages.Add(new List<PrintLine>());
                used = Margin * 2;
            }
            pages[^1].Add(line);
            used += height;
        }

        using var stream = new SKDynamicMemoryWStream();
        using (var pdf = SKDocument.CreatePdf(stream))
        using (var renderer = new LineRenderer())
        {
            foreach (var pageLines in pages)
            {
                // A4 pages are always full size; a receipt page is only as tall as its content.
                var (pageWidthPoints, pageHeightPoints) = paper == PdfPaper.A4
                    ? (A4WidthPoints, A4HeightPoints)
                    : (PaperWidthDots * scale, (Margin * 2 + pageLines.Sum(LineHeight)) * scale);

                using var canvas = pdf.BeginPage(pageWidthPoints, pageHeightPoints);
                canvas.Translate(offsetX, offsetY);
                canvas.Scale(scale);

                var y = Margin;
                foreach (var line in pageLines)
                    renderer.Draw(canvas, line, ref y);

                pdf.EndPage();
            }
            pdf.Close();
        }

        using var data = stream.DetachAsData();
        return data.ToArray();
    }

    public string BuildPreviewText(Receipt receipt, StoreSettings? storeSettings = null)
    {
        var sb = new StringBuilder();
        foreach (var line in BuildLines(receipt, storeSettings))
        {
            sb.AppendLine(line.Kind switch
            {
                PrintLineKind.Separator => new string('-', TextColumns),
                PrintLineKind.Spacer => "",
                _ when line.Right != null => FormatRow(line.Text, line.Right),
                _ when line.Align == PrintAlign.Center => line.Text.PadLeft((TextColumns + line.Text.Length) / 2),
                _ => line.Text,
            });
        }
        return sb.ToString();
    }

    public bool RequiresImage(string text) => !CanPrintAsText(text);

    public byte[] BuildTextImage(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(t => t.Length == 0 ? PrintLine.Spacer() : PrintLine.Plain(t))
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

    /// <summary>
    /// The receipt's content and order, kept in one place so the rendering paths
    /// (text, image, PDF) never drift apart.
    /// </summary>
    private static List<PrintLine> BuildLines(Receipt receipt, StoreSettings? storeSettings)
    {
        var lines = new List<PrintLine>();
        var notation = receipt.Currency?.Notation ?? "";
        string Money(decimal amount) => $"{notation}{amount.ToString("F2", Inv)}";
        void Row(string label, string value, PrintLineKind kind = PrintLineKind.Normal) =>
            lines.Add(new PrintLine(label, PrintAlign.Left, kind, value));
        void Centered(string? text)
        {
            if (!string.IsNullOrEmpty(text))
                lines.Add(PrintLine.Centered(text));
        }
        void Separator() => lines.Add(PrintLine.Separator());

        // Header
        if (storeSettings != null)
        {
            lines.Add(PrintLine.Header(storeSettings.StoreName));
            Centered(storeSettings.AddressLine1);
            Centered(storeSettings.AddressLine2);
            if (!string.IsNullOrEmpty(storeSettings.Phone))
                Centered($"Phone: {storeSettings.Phone}");
            if (!string.IsNullOrEmpty(storeSettings.TaxId))
                Centered($"Tax ID: {storeSettings.TaxId}");
        }
        else
        {
            lines.Add(PrintLine.Header("RECEIPT"));
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
            lines.Add(PrintLine.Bold(item.ProductName));

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
        Row("TOTAL:", Money(receipt.TotalAmount), PrintLineKind.Bold);

        // Payment
        Separator();
        if (!string.IsNullOrEmpty(receipt.PaymentMethod?.Name))
            Row("Payment:", receipt.PaymentMethod.Name);
        if (receipt.AmountPaid > 0)
            Row("Paid:", Money(receipt.AmountPaid));
        if (receipt.ChangeAmount > 0)
            Row("Change:", Money(receipt.ChangeAmount));

        // Footer
        lines.Add(PrintLine.Spacer());
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

    private static byte[] EncodeLinesAsText(IReadOnlyList<PrintLine> lines)
    {
        var sb = new StringBuilder();
        const string ESC = "\x1B";
        const string GS = "\x1D";

        sb.Append(ESC + "@");
        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case PrintLineKind.Separator:
                    sb.Append(ESC + "a\x00");
                    sb.AppendLine(new string('-', TextColumns));
                    continue;
                case PrintLineKind.Spacer:
                    sb.AppendLine();
                    continue;
                case PrintLineKind.Header:
                    sb.Append(ESC + "a\x01");
                    sb.Append(GS + "!\x11"); // Double height and width
                    sb.AppendLine(line.Text);
                    sb.Append(GS + "!\x00");
                    continue;
            }

            sb.Append(ESC + "a" + (line.Right == null && line.Align == PrintAlign.Center ? "\x01" : "\x00"));
            if (line.Kind == PrintLineKind.Bold) sb.Append(ESC + "E\x01");
            sb.AppendLine(line.Right != null ? FormatRow(line.Text, line.Right) : line.Text);
            if (line.Kind == PrintLineKind.Bold) sb.Append(ESC + "E\x00");
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
    internal static bool ContainsArabic(string? text)
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

    private static byte[] EncodeLinesAsImage(IReadOnlyList<PrintLine> lines)
    {
        using var bitmap = RenderBitmap(lines);
        return EncodeRaster(bitmap);
    }

    // Height is computed analytically (font size * fixed spacing per line) rather than by
    // an actual measure pass - lines that are too wide shrink their font instead of
    // wrapping, so every line keeps its fixed height and this stays a single drawing pass.
    private static float LineHeight(PrintLine line) => line.Kind switch
    {
        PrintLineKind.Header => HeaderFontSize * LineSpacing,
        PrintLineKind.Separator => NormalFontSize * 0.7f,
        PrintLineKind.Spacer => NormalFontSize * 0.6f,
        _ => NormalFontSize * LineSpacing,
    };

    private static SKBitmap RenderBitmap(IReadOnlyList<PrintLine> lines)
    {
        var height = Margin * 2 + lines.Sum(LineHeight);

        using var surface = SKSurface.Create(new SKImageInfo(PaperWidthDots, (int)Math.Ceiling(height), SKColorType.Rgba8888, SKAlphaType.Opaque));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var renderer = new LineRenderer();
        var y = Margin;
        foreach (var line in lines)
            renderer.Draw(canvas, line, ref y);

        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    /// <summary>
    /// Draws lines onto a canvas laid out in printer dots (576 across). The receipt image and
    /// the PDF both go through this, so a PDF looks exactly like the slip.
    /// </summary>
    private sealed class LineRenderer : IDisposable
    {
        private const float UsableWidth = PaperWidthDots - Margin * 2;
        private const float ColumnGap = 12f;

        private readonly SKTypeface _latinTypeface = SKTypeface.Default;
        // MatchCharacter asks the OS font system for whichever installed font actually has
        // Arabic glyphs (varies by machine/Windows language pack) rather than betting on one
        // hardcoded font name being present.
        private readonly SKTypeface _arabicTypeface = SKFontManager.Default.MatchCharacter(0x0627)
            ?? SKFontManager.Default.MatchFamily("Tahoma")
            ?? SKTypeface.Default;
        private readonly SKShaper _arabicShaper;
        private readonly SKPaint _linePaint = new() { Color = SKColors.Black, StrokeWidth = 2, IsAntialias = true };
        private readonly SKPaint _paint = new() { Color = SKColors.Black, IsAntialias = true };

        public LineRenderer()
        {
            _arabicShaper = new SKShaper(_arabicTypeface);
        }

        public void Dispose()
        {
            _arabicShaper.Dispose();
            _linePaint.Dispose();
            _paint.Dispose();
            _arabicTypeface.Dispose();
            _latinTypeface.Dispose();
        }

        private SKFont MakeFont(string text, float size, bool bold) =>
            new(ContainsArabic(text) ? _arabicTypeface : _latinTypeface, size) { Embolden = bold };

        private void DrawText(SKCanvas canvas, string text, float x, float y, SKTextAlign align, SKFont font)
        {
            if (ContainsArabic(text))
                canvas.DrawShapedText(_arabicShaper, text, x, y, align, font, _paint);
            else
                canvas.DrawText(text, x, y, align, font, _paint);
        }

        /// <summary>Draws one line with its top at <paramref name="y"/> and moves <paramref name="y"/> past it.</summary>
        public void Draw(SKCanvas canvas, PrintLine line, ref float y)
        {
            if (line.Kind == PrintLineKind.Separator)
            {
                y += NormalFontSize * 0.35f;
                canvas.DrawLine(Margin, y, PaperWidthDots - Margin, y, _linePaint);
                y += NormalFontSize * 0.35f;
                return;
            }

            if (line.Kind == PrintLineKind.Spacer)
            {
                y += NormalFontSize * 0.6f;
                return;
            }

            var lineBoxSize = line.Kind == PrintLineKind.Header ? HeaderFontSize : NormalFontSize;
            var bold = line.Kind == PrintLineKind.Bold;

            using var leftFont = MakeFont(line.Text, lineBoxSize, bold);
            using var rightFont = MakeFont(line.Right ?? "", lineBoxSize, bold);

            // Shrink (both columns together) until the line fits the paper, rather than
            // letting a long product name run off the edge.
            float Needed() => leftFont.MeasureText(line.Text) +
                (line.Right != null ? ColumnGap + rightFont.MeasureText(line.Right) : 0);
            while (Needed() > UsableWidth && leftFont.Size > MinFontSize)
            {
                leftFont.Size -= 1;
                rightFont.Size -= 1;
            }

            y += lineBoxSize; // baseline sits at the bottom of this line's box

            if (line.Right != null)
            {
                DrawText(canvas, line.Text, Margin, y, SKTextAlign.Left, leftFont);
                DrawText(canvas, line.Right, PaperWidthDots - Margin, y, SKTextAlign.Right, rightFont);
            }
            else
            {
                // Arabic text is right-aligned regardless of the line's nominal alignment - a
                // left-aligned "field" of Arabic text should still start from the paper's right
                // edge and read right-to-left, matching how a receipt in an RTL language is
                // actually laid out rather than how the (LTR-authored) line list positions it.
                var align = ContainsArabic(line.Text) && line.Align == PrintAlign.Left ? PrintAlign.Right : line.Align;
                var (x, skAlign) = align switch
                {
                    PrintAlign.Center => (PaperWidthDots / 2f, SKTextAlign.Center),
                    PrintAlign.Right => (PaperWidthDots - Margin, SKTextAlign.Right),
                    _ => (Margin, SKTextAlign.Left),
                };
                DrawText(canvas, line.Text, x, y, skAlign, leftFont);
            }

            y += lineBoxSize * (LineSpacing - 1f);
        }
    }

    /// <summary>
    /// Encodes a bitmap as ESC/POS "GS v 0" raster print commands - the modern, simpler raster
    /// command supported by virtually all ESC/POS thermal printers (replaces the old strip-based
    /// "ESC *" bit-image mode this file used to build). Tall images go out as consecutive bands.
    /// </summary>
    private static byte[] EncodeRaster(SKBitmap bitmap)
    {
        var widthBytes = (bitmap.Width + 7) / 8;

        var result = new List<byte>(widthBytes * bitmap.Height + 64);
        result.AddRange(new byte[] { 0x1B, 0x40 }); // Initialize printer

        for (var top = 0; top < bitmap.Height; top += RasterBandRows)
        {
            var rows = Math.Min(RasterBandRows, bitmap.Height - top);
            var data = new byte[widthBytes * rows];

            for (int py = 0; py < rows; py++)
            {
                for (int px = 0; px < bitmap.Width; px++)
                {
                    var pixel = bitmap.GetPixel(px, top + py);
                    var luminance = (pixel.Red * 299 + pixel.Green * 587 + pixel.Blue * 114) / 1000;
                    if (luminance < 160)
                    {
                        data[py * widthBytes + (px >> 3)] |= (byte)(0x80 >> (px & 7));
                    }
                }
            }

            result.AddRange(new byte[]
            {
                0x1D, (byte)'v', (byte)'0', 0, // GS v 0, normal mode
                (byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF),
                (byte)(rows & 0xFF), (byte)((rows >> 8) & 0xFF),
            });
            result.AddRange(data);
        }

        result.AddRange(new byte[] { 0x1B, (byte)'d', FeedLinesBeforeCut }); // Feed past the cutter
        result.AddRange(new byte[] { 0x1D, (byte)'V', 0x01 }); // Cut paper

        return result.ToArray();
    }
}
