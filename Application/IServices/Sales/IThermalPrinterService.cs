using System.Drawing;
using Core.Entities.Sales;

namespace Application.IServices.Sales;

public interface IThermalPrinterService
{
    bool IsPrinterAvailable(string? printerName = null);
    Task<bool> PrintReceiptAsync(Receipt receipt, string? printerName = null);
    Task<bool> PrintTextAsync(string text, string? printerName = null);
    Task<bool> PrintDirectReceiptAsync(Order order, string? printerName = null);
    string PreviewReceiptContent(Order order);

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
