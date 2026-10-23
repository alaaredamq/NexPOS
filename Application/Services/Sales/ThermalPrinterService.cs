using System.Globalization;
using System.Text;
using Application.IServices.Sales;
using Application.IServices.Settings;
using Core.Entities.Configuration;
using Core.Entities.Sales;
using Core.IRepositories.PrinterTransport;
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
    {
        // Callers often hold a receipt without its navigation properties (e.g. the one
        // CreateReceiptAsync just saved, or one from GetAllAsync) - reload it fully so the
        // printout has its items, cashier, payment method and currency.
        if (receipt.Id > 0)
            receipt = await _receiptRepository.GetByIdAsync(receipt.Id) ?? receipt;

        // Get store settings for receipt printing
        var storeSettings = await _storeSettingsService.GetStoreSettingsEntityAsync();
        var bytes = _escPos.BuildReceipt(receipt, storeSettings);
        return _printer.SendBytes(printerName ?? await ResolveDefaultPrinterAsync(), bytes, "POS Receipt");
    }

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

    public async Task<bool> PrintDirectReceiptAsync(Order order, string? printerName = null)
    {
        try
        {
            if (order == null)
            {
                Console.WriteLine($"[THERMAL SERVICE ERROR] Order is null");
                return false;
            }

            printerName ??= await ResolveDefaultPrinterAsync();

            Console.WriteLine($"[THERMAL SERVICE] Creating direct ESC/POS receipt for order {order.Id}...");
            Console.WriteLine($"[THERMAL SERVICE] Primary printer: {printerName}");
            Console.WriteLine($"[THERMAL SERVICE] Order total: ${order.TotalAmount.ToString("F2", CultureInfo.InvariantCulture)}");
            
            // Create ESC/POS commands directly - same approach as BuildReceipt but for Order
            var escPosBytes = await BuildDirectOrderReceiptAsync(order);
            
            if (escPosBytes == null || escPosBytes.Length == 0)
            {
                Console.WriteLine($"[THERMAL SERVICE ERROR] Generated bytes are null or empty");
                return false;
            }
            
            Console.WriteLine($"[THERMAL SERVICE] Generated {escPosBytes.Length} ESC/POS bytes");
            
            if (_printer == null)
            {
                Console.WriteLine($"[THERMAL SERVICE ERROR] Printer transport is null");
                return false;
            }

            // Check if primary printer is available
            Console.WriteLine($"[THERMAL SERVICE] Checking if printer '{printerName}' is available...");
            if (!_printer.IsAvailable(printerName))
            {
                Console.WriteLine($"[THERMAL SERVICE] ❌ Primary printer '{printerName}' not available");
                
                // Try to find an alternative printer
                var alternativePrinter = FindAlternativePrinter();
                if (alternativePrinter != null)
                {
                    Console.WriteLine($"[THERMAL SERVICE] 🔄 Trying alternative printer: {alternativePrinter}");
                    printerName = alternativePrinter;
                }
                else
                {
                    Console.WriteLine($"[THERMAL SERVICE] ❌ No alternative printers found");
                    return false;
                }
            }
            else
            {
                Console.WriteLine($"[THERMAL SERVICE] ✅ Printer '{printerName}' is available");
            }
            
            var result = _printer.SendBytes(printerName, escPosBytes, "Direct Order Receipt");
            Console.WriteLine($"[THERMAL SERVICE] Direct receipt print result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[THERMAL SERVICE ERROR] Exception in PrintDirectReceiptAsync: {ex.Message}");
            Console.WriteLine($"[THERMAL SERVICE ERROR] Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    private string? FindAlternativePrinter()
    {
        try
        {
            Console.WriteLine($"[THERMAL SERVICE] Searching for alternative printers...");
            
            var installedPrinters = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList();
            Console.WriteLine($"[THERMAL SERVICE] Found {installedPrinters.Count} installed printers");
            
            // Look for common thermal printer names
            var thermalPrinterKeywords = new[] { "POS", "Thermal", "Receipt", "XPrinter", "Epson", "Star", "Citizen" };
            
            foreach (var printer in installedPrinters)
            {
                Console.WriteLine($"[THERMAL SERVICE] Checking printer: {printer}");
                
                if (thermalPrinterKeywords.Any(keyword => printer.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    if (_printer.IsAvailable(printer))
                    {
                        Console.WriteLine($"[THERMAL SERVICE] ✅ Found available thermal printer: {printer}");
                        return printer;
                    }
                }
            }
            
            // If no thermal printer found, try the default system printer
            var defaultPrinter = new System.Drawing.Printing.PrinterSettings().PrinterName;
            if (!string.IsNullOrEmpty(defaultPrinter) && _printer.IsAvailable(defaultPrinter))
            {
                Console.WriteLine($"[THERMAL SERVICE] ✅ Using default system printer: {defaultPrinter}");
                return defaultPrinter;
            }
            
            Console.WriteLine($"[THERMAL SERVICE] ❌ No suitable alternative printer found");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[THERMAL SERVICE] Error finding alternative printer: {ex.Message}");
            return null;
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

    private async Task<byte[]> BuildDirectOrderReceiptAsync(Order order)
    {
        var receipt = await GetReceiptForOrderAsync(order);
        var storeSettings = await _storeSettingsService.GetStoreSettingsEntityAsync();
        return _escPos.BuildReceipt(receipt, storeSettings);
    }
}
