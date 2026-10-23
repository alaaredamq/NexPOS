using Core.Entities.Configuration;
using Core.Entities.Sales;
using Core.Printing;

namespace Core.IRepositories.PrinterTransport;

public interface IEscPosBuilder
{
    /// <summary>
    /// Builds a ready-to-send ESC/POS print job for the receipt. Uses fast plain-text mode
    /// (codepage 437) when every character on the receipt fits that code page; otherwise
    /// renders the receipt as an image (via SkiaSharp, with proper Arabic shaping/RTL) - e.g.
    /// Arabic store/customer/item names or a currency symbol like "ل.ل", which codepage 437
    /// would print as "?".
    /// </summary>
    byte[] BuildReceipt(Receipt receipt, StoreSettings? storeSettings = null);

    /// <summary>The receipt's lines, ready to be printed (<see cref="BuildPrintJob"/>) or saved as a PDF (<see cref="BuildPdf"/>).</summary>
    PrintDocument BuildReceiptDocument(Receipt receipt, StoreSettings? storeSettings = null);

    /// <summary>ESC/POS job for any document - same text-vs-image choice as <see cref="BuildReceipt"/>.</summary>
    byte[] BuildPrintJob(PrintDocument document);

    /// <summary>
    /// The document as a PDF with the same layout (and Arabic shaping) as the printed slip.
    /// Receipt paper gives one receipt-width page; A4 paginates long reports.
    /// </summary>
    byte[] BuildPdf(PrintDocument document, PdfPaper paper = PdfPaper.Receipt);

    /// <summary>
    /// The same receipt layout as <see cref="BuildReceipt"/>, as plain Unicode text for
    /// on-screen previews.
    /// </summary>
    string BuildPreviewText(Receipt receipt, StoreSettings? storeSettings = null);

    /// <summary>True when the text has characters codepage 437 can't print (e.g. Arabic).</summary>
    bool RequiresImage(string text);

    /// <summary>Renders free-form text (one printed line per text line) as an image print job.</summary>
    byte[] BuildTextImage(string text);
}
