namespace Core.Printing;

public enum PrintLineKind { Header, Normal, Bold, Separator, Spacer }

public enum PrintAlign { Left, Center, Right }

/// <summary>
/// One printed line. When <paramref name="Right"/> is set the line is a two-column row:
/// <paramref name="Text"/> on the left edge, <paramref name="Right"/> on the right edge.
/// </summary>
public record PrintLine(string Text, PrintAlign Align, PrintLineKind Kind, string? Right = null)
{
    public static PrintLine Header(string text) => new(text, PrintAlign.Center, PrintLineKind.Header);
    public static PrintLine Centered(string text) => new(text, PrintAlign.Center, PrintLineKind.Normal);
    public static PrintLine Plain(string text) => new(text, PrintAlign.Left, PrintLineKind.Normal);
    public static PrintLine Bold(string text) => new(text, PrintAlign.Left, PrintLineKind.Bold);
    public static PrintLine Row(string label, string value, bool bold = false) =>
        new(label, PrintAlign.Left, bold ? PrintLineKind.Bold : PrintLineKind.Normal, value);
    public static PrintLine Separator() => new("", PrintAlign.Left, PrintLineKind.Separator);
    public static PrintLine Spacer() => new("", PrintAlign.Left, PrintLineKind.Spacer);
}

/// <summary>
/// Paper the PDF is laid out for. The same lines are rendered either as a receipt-width strip
/// (what the thermal printer prints) or paginated onto A4 for long reports.
/// </summary>
public enum PdfPaper { Receipt, A4 }

/// <summary>
/// Printable content that isn't tied to one output: the same lines become an ESC/POS job for
/// the receipt printer or a PDF file, so "Print" and "PDF" never show different content.
/// </summary>
public record PrintDocument(string Title, IReadOnlyList<PrintLine> Lines)
{
    /// <summary>File-name-safe version of <see cref="Title"/>.</summary>
    public string FileName
    {
        get
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var cleaned = new string(Title.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(cleaned) ? "document" : cleaned;
        }
    }
}
