using System.Drawing;
using Core.Entities.Sales;
using Core.Printing;

namespace Application.IServices.Sales;

public interface IThermalPrinterService
{
    bool IsPrinterAvailable(string? printerName = null);
    /// <param name="quickFail">The automatic print after a sale: don't make the cashier wait on a printer that just failed (see <c>IPrinterTransport.SendBytes</c>).</param>
    Task<bool> PrintReceiptAsync(Receipt receipt, string? printerName = null, bool quickFail = false);
    Task<bool> PrintTextAsync(string text, string? printerName = null);
    Task<bool> PrintDirectReceiptAsync(Order order, string? printerName = null);
    string PreviewReceiptContent(Order order);

    /// <summary>The printable lines of a receipt, reloaded with its items, cashier and currency.</summary>
    Task<PrintDocument> GetReceiptDocumentAsync(Receipt receipt);

    /// <summary>The printable lines of an order's receipt (its stored receipt, or one built from the order).</summary>
    Task<PrintDocument> GetOrderReceiptDocumentAsync(Order order);

    /// <summary>
    /// Sends any document to the receipt printer chosen in Settings (or <paramref name="printerName"/>).
    /// Returns false - never throws - when the printer rejects the job or isn't there.
    /// </summary>
    Task<bool> PrintDocumentAsync(PrintDocument document, string? printerName = null, string jobName = "NexPOS Document", bool quickFail = false);

    /// <summary>The same document as a PDF file - what the "PDF" option of every print menu returns.</summary>
    byte[] GeneratePdf(PrintDocument document, PdfPaper paper = PdfPaper.Receipt);

    /// <summary>
    /// Lists printers installed on this machine, for the Settings printer picker.
    /// </summary>
    List<string> GetAvailablePrinters();

    /// <summary>
    /// Prints a short test slip to the given printer, so a client can confirm their choice
    /// from Settings without ringing up a real sale.
    /// </summary>
    Task<bool> TestPrintAsync(string printerName);
}
