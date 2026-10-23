using System.Globalization;
using System.Text;
using Application.IServices.Sales;
using Application.IServices.Settings;
using Core.Entities.Configuration;
using Core.Entities.Sales;
using Core.IRepositories.PrinterTransport;
using Core.Printing;
using Core.IRepositories.Sales;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales;

public class ThermalPrinterService : IThermalPrinterService
{
    private readonly IPrinterTransport _printer;
    private readonly IEscPosBuilder _escPos;
    private readonly IStoreSettingsService _storeSettingsService;
    private readonly ISystemSettingsService _systemSettingsService;
    private readonly IReceiptRepository _receiptRepository;

    /// <summary>
    /// Used only when no printer is configured in Settings yet (e.g. right after a fresh
    /// install). Once a client picks a printer on the Settings page, that choice always wins.
    /// </summary>
    private const string FallbackPrinter = "POSPrinter POS80";
    private const string ReceiptPrinterSettingKey = "ReceiptPrinterName";

    public ThermalPrinterService(
        IPrinterTransport printer,
        IEscPosBuilder escPos,
        IStoreSettingsService storeSettingsService,
        ISystemSettingsService systemSettingsService,
        IReceiptRepository receiptRepository)
    {
        _printer = printer;
        _escPos = escPos;
        _storeSettingsService = storeSettingsService;
        _systemSettingsService = systemSettingsService;
        _receiptRepository = receiptRepository;
    }

    /// <summary>
    /// Resolves the printer to use when a call site doesn't pass one explicitly: the printer
    /// configured on the Settings page, falling back to <see cref="FallbackPrinter"/> if none
    /// has been set yet.
    /// </summary>
    private async Task<string> ResolveDefaultPrinterAsync()
    {
        var configured = await _systemSettingsService.GetSettingValueAsync(ReceiptPrinterSettingKey, "");
        return string.IsNullOrWhiteSpace(configured) ? FallbackPrinter : configured;
    }

    public List<string> GetAvailablePrinters() => _printer.GetAvailablePrinters();

    public async Task<bool> TestPrintAsync(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return false;

        var testText = "TEST PRINT\n" +
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                        "If you can read this,\n" +
                        "the printer is set up\n" +
                        "correctly.\n\n\n";
        return await PrintTextAsync(testText, printerName);
    }

    public bool IsPrinterAvailable(string? printerName = null)
        => _printer.IsAvailable(printerName ?? FallbackPrinter);

    public async Task<bool> PrintReceiptAsync(Receipt receipt, string? printerName = null)
        => await PrintDocumentAsync(await GetReceiptDocumentAsync(receipt), printerName, "POS Receipt");

    public async Task<PrintDocument> GetReceiptDocumentAsync(Receipt receipt)
    {
        // Callers often hold a receipt without its navigation properties (e.g. the one
        // CreateReceiptAsync just saved, or one from GetAllAsync) - reload it fully so the
        // printout has its items, cashier, payment method and currency.
        if (receipt.Id > 0)
            receipt = await _receiptRepository.GetByIdAsync(receipt.Id) ?? receipt;

        var storeSettings = await _storeSettingsService.GetStoreSettingsEntityAsync();
        return _escPos.BuildReceiptDocument(receipt, storeSettings);
    }

    public async Task<PrintDocument> GetOrderReceiptDocumentAsync(Order order)
        => await GetReceiptDocumentAsync(await GetReceiptForOrderAsync(order));

    public async Task<bool> PrintDocumentAsync(PrintDocument document, string? printerName = null, string jobName = "NexPOS Document")
    {
        try
        {
            var bytes = _escPos.BuildPrintJob(document);
            return _printer.SendBytes(printerName ?? await ResolveDefaultPrinterAsync(), bytes, jobName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[THERMAL SERVICE ERROR] Printing '{document.Title}' failed: {ex.Message}");
            return false;
        }
    }

    public byte[] GeneratePdf(PrintDocument document, PdfPaper paper = PdfPaper.Receipt)
        => _escPos.BuildPdf(document, paper);

    public async Task<bool> PrintTextAsync(string text, string? printerName = null)
    {
        printerName ??= await ResolveDefaultPrinterAsync();

        // Codepage 437 would turn Arabic (or any other non-437 character) into "?" -
        // print those as an image instead.
        if (_escPos.RequiresImage(text))
            return _printer.SendBytes(printerName, _escPos.BuildTextImage(text), "Text Receipt");

        Console.WriteLine($"[THERMAL SERVICE] Printing pure text directly...");
        Console.WriteLine($"[THERMAL SERVICE] Text preview: {text.Substring(0, Math.Min(200, text.Length))}...");

        // Convert text to ESC/POS commands for better formatting
        var escPosText = ConvertTextToEscPos(text);
        
        var result = _printer.SendString(printerName, escPosText, "Text Receipt");
        Console.WriteLine($"[THERMAL SERVICE] Text print result: {result}");
        return result;
    }

    private string ConvertTextToEscPos(string text)
    {
        var sb = new StringBuilder();
        const string ESC = "\x1B";
        const string GS = "\x1D";

        // Initialize printer
        sb.Append(ESC + "@");
        
        // Set character encoding
        sb.Append(ESC + "t\x00"); // Select character table
        
        var lines = text.Split('\n');
        
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine();
                continue;
            }
            
            // Check for formatting hints - use case-insensitive matching for store name and thank you
            var lineUpper = line.ToUpperInvariant();
            if (lineUpper.Contains("THANK YOU"))
            {
                // Center align and bold
                sb.Append(ESC + "a\x01"); // Center align
                sb.Append(ESC + "E\x01"); // Bold on
                sb.AppendLine(line);
                sb.Append(ESC + "E\x00"); // Bold off
                sb.Append(ESC + "a\x00"); // Left align
            }
            else if (line.Contains("TOTAL:"))
            {
                // Bold for total
                sb.Append(ESC + "E\x01"); // Bold on
                sb.AppendLine(line);
                sb.Append(ESC + "E\x00"); // Bold off
            }
            else if (line.Contains("---"))
            {
                // Dashed lines
                sb.AppendLine(line);
            }
            else
            {
                // Regular text
                sb.AppendLine(line);
            }
        }
        
        // Cut paper
        sb.Append(GS + "V\x01");
        
        return sb.ToString();
    }

    public string PreviewReceiptContent(Order order)
    {
        try
        {
            Console.WriteLine($"[RECEIPT PREVIEW] Generating preview for order {order?.Id}");
            
            if (order == null) return "ERROR: Order is null";

            // Same layout the printer gets, as Unicode text so Arabic shows correctly on screen.
            var receipt = GetReceiptForOrderAsync(order).GetAwaiter().GetResult();
            var storeSettings = _storeSettingsService.GetStoreSettingsEntityAsync().GetAwaiter().GetResult();
            var preview = _escPos.BuildPreviewText(receipt, storeSettings);

            Console.WriteLine($"[RECEIPT PREVIEW] Generated preview with {preview.Length} characters");
            return preview;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RECEIPT PREVIEW ERROR] {ex.Message}");
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Prints an order's receipt to the configured receipt printer. Deliberately no fallback to
    /// "some other printer": the Windows default is often a PDF writer, which would turn a
    /// failed print into a surprise file - the caller reports the failure instead.
    /// </summary>
    public async Task<bool> PrintDirectReceiptAsync(Order order, string? printerName = null)
    {
        try
        {
            if (order == null)
            {
                Console.WriteLine($"[THERMAL SERVICE ERROR] Order is null");
                return false;
            }

            var document = await GetOrderReceiptDocumentAsync(order);
            var printed = await PrintDocumentAsync(document, printerName, "Direct Order Receipt");
            Console.WriteLine($"[THERMAL SERVICE] Direct receipt print for order {order.Id}: {printed}");
            return printed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[THERMAL SERVICE ERROR] Exception in PrintDirectReceiptAsync: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The receipt to print for an order: its stored Receipt row when there is one (real
    /// cashier, payment method and change), otherwise an unsaved one built from the order
    /// for orders that never got a receipt.
    /// </summary>
    private async Task<Receipt> GetReceiptForOrderAsync(Order order)
    {
        var stored = (await _receiptRepository.GetByOrderIdAsync(order.Id)).FirstOrDefault(r => !r.IsVoid);
        return stored ?? ReceiptFromOrder(order);
    }

    private static Receipt ReceiptFromOrder(Order order)
    {
        var cartItems = order.Cart?.CartItems?.Where(ci => ci != null).ToList()
            ?? new List<Core.Entities.Inventory.CartItem>();

        var receipt = new Receipt(
            $"ORD-{order.Id:D6}", order.Id, order.CustomerId, cashierId: 0,
            subTotal: cartItems.Sum(ci => ci.TotalPrice), taxAmount: 0,
            discountAmount: order.Cart?.DiscountAmount ?? 0, totalAmount: order.TotalAmount,
            paymentMethodId: 0, currencyId: order.CurrencyId)
        {
            ReceiptDate = order.OrderDate,
            Customer = order.Customer,
            Currency = order.Currency,
        };

        foreach (var ci in cartItems)
        {
            // Weighed/volume products carry the real amount in WeightOrVolume.
            var quantity = ci.WeightOrVolume is > 0 ? ci.WeightOrVolume.Value : ci.Quantity;
            receipt.ReceiptItems.Add(new ReceiptItem(0, ci.ProductId, ci.Product?.Name ?? "Unknown Item",
                quantity, ci.UnitPrice, 0, ci.Product?.Barcode, ci.Product?.Unit, ci.WeightOrVolume)
            {
                LineTotal = ci.TotalPrice, // what the customer was actually charged
            });
        }

        return receipt;
    }
}
