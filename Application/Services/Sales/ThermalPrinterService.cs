using System.Globalization;
using System.Text;
using Application.IServices.Sales;
using Application.IServices.Settings;
using Core.Entities.Configuration;
using Core.Entities.Sales;
using Core.IRepositories.PrinterTransport;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales;

public class ThermalPrinterService : IThermalPrinterService
{
    private readonly IPrinterTransport _printer;
    private readonly IEscPosBuilder _escPos;
    private readonly IStoreSettingsService _storeSettingsService;
    private readonly ISystemSettingsService _systemSettingsService;

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
        ISystemSettingsService systemSettingsService)
    {
        _printer = printer;
        _escPos = escPos;
        _storeSettingsService = storeSettingsService;
        _systemSettingsService = systemSettingsService;
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
        // Get store settings for receipt printing
        var storeSettings = await _storeSettingsService.GetStoreSettingsEntityAsync();
        var bytes = _escPos.BuildReceipt(receipt, storeSettings);
        return _printer.SendBytes(printerName ?? await ResolveDefaultPrinterAsync(), bytes, "POS Receipt");
    }

    public async Task<bool> PrintTextAsync(string text, string? printerName = null)
    {
        printerName ??= await ResolveDefaultPrinterAsync();

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

            var receiptBytes = BuildDirectOrderReceipt(order);
            var receiptText = Encoding.GetEncoding(437).GetString(receiptBytes);
            
            // Clean up ESC/POS commands for preview
            var preview = receiptText
                .Replace("\x1B@", "[INIT]")
                .Replace("\x1B" + "a\x01", "[CENTER]")
                .Replace("\x1B" + "a\x00", "[LEFT]")
                .Replace("\x1D" + "!\x11", "[LARGE]")
                .Replace("\x1D" + "!\x00", "[NORMAL]")
                .Replace("\x1B" + "E\x01", "[BOLD]")
                .Replace("\x1B" + "E\x00", "[/BOLD]")
                .Replace("\x1D" + "V\x01", "[CUT]");

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

    private byte[] BuildDirectOrderReceipt(Order order)
    {
        return BuildDirectOrderReceiptAsync(order).GetAwaiter().GetResult();
    }

    private async Task<byte[]> BuildDirectOrderReceiptAsync(Order order)
    {
        try
        {
            Console.WriteLine($"[BUILD RECEIPT] Starting receipt generation for order {order?.Id}");
            
            if (order == null)
            {
                Console.WriteLine($"[BUILD RECEIPT ERROR] Order is null!");
                throw new ArgumentNullException(nameof(order), "Order cannot be null");
            }

            // Get store settings from database
            var storeSettings = await _storeSettingsService.GetStoreSettingsEntityAsync();

            var sb = new StringBuilder();
            const string ESC = "\x1B";
            const string GS = "\x1D";

            // Initialize printer (reduced top margin - start printing immediately)
            sb.Append(ESC + "@");
            
            // Store header - start immediately without extra spacing
            sb.Append(ESC + "a\x01"); // Center align
            sb.Append(GS + "!\x11"); // Double height and width
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
            sb.Append(ESC + "a\x00"); // Left align
            
            sb.AppendLine("================================================");
            
            // Receipt Meta - use full width formatting
            var receiptNumber = $"ORD-{order.Id:D6}";
            var receiptLine = FormatTotalLine("Receipt #:", receiptNumber);
            sb.AppendLine(receiptLine);
            
            var dateLine = FormatTotalLine("Date:", order.OrderDate.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine(dateLine);
            
            var customerName = $"{order.Customer?.FirstName} {order.Customer?.LastName}".Trim();
            if (string.IsNullOrEmpty(customerName))
            {
                customerName = "Walk-in Customer";
            }
            var customerLine = FormatTotalLine("Customer:", customerName);
            sb.AppendLine(customerLine);
            
            var cashierLine = FormatTotalLine("Cashier:", "System");
            sb.AppendLine(cashierLine);
            
            sb.AppendLine("================================================");
            
            // Items section header
            sb.AppendLine("ITEMS");
            sb.AppendLine("------------------------------------------------");
            
            // Items - exactly like Receipt.cshtml structure
            Console.WriteLine($"[BUILD RECEIPT] Processing cart items...");
            
            if (order.Cart?.CartItems != null && order.Cart.CartItems.Any())
            {
                Console.WriteLine($"[BUILD RECEIPT] Processing {order.Cart.CartItems.Count} items");
                
                foreach (var item in order.Cart.CartItems)
                {
                    if (item == null)
                    {
                        Console.WriteLine($"[BUILD RECEIPT] Skipping null cart item");
                        continue;
                    }
                    
                    // Item name (bold in Receipt.cshtml)
                    var name = item.Product?.Name ?? "Unknown Item";
                    if (name.Length > 30) name = name.Substring(0, 27) + "...";
                    sb.Append(ESC + "E\x01"); // Bold on
                    sb.AppendLine(name);
                    sb.Append(ESC + "E\x00"); // Bold off
                    
                    // Quantity x Price = Total (using full width)
                    // For weight/volume products, use WeightOrVolume; for piece products, use Quantity
                    decimal displayQuantity = item.WeightOrVolume.HasValue && item.WeightOrVolume.Value > 0 
                        ? item.WeightOrVolume.Value 
                        : item.Quantity;
                    var quantityDisplay = displayQuantity.ToString("F3");
                    var unit = item.Product?.Unit;
                    if (!string.IsNullOrEmpty(unit) && unit.ToLower() != "piece")
                    {
                        quantityDisplay += $" {unit}";
                    }
                    var qtyPrice = $"{quantityDisplay} x ${item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)}";
                    var total = $"${item.TotalPrice.ToString("F2", CultureInfo.InvariantCulture)}";
                    var itemTotalLine = FormatTotalLine(qtyPrice, total);
                    sb.AppendLine(itemTotalLine);
                    
                    sb.AppendLine($"");
                    
                    // Reduced spacing between products to save space
                    // (since we added more bottom margin)
                }
            }
            else
            {
                Console.WriteLine($"[BUILD RECEIPT] No cart items found");
                sb.AppendLine("No items");
                sb.AppendLine();
            }
            
            sb.AppendLine("================================================");
            
            // Totals - use full page width (48 characters for thermal printer)
            var items = order.Cart?.CartItems?.ToList() ?? new List<Core.Entities.Inventory.CartItem>();
            var subtotal = items.Sum(i => i.TotalPrice);
            var discountAmount = order.Cart?.DiscountAmount ?? 0;
            var cartTotal = subtotal - discountAmount;
            var adjustment = order.TotalAmount - cartTotal;
            
            // Format totals with proper spacing across full width
            var subtotalLine = FormatTotalLine("Subtotal:", $"${subtotal.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine(subtotalLine);
            
            if (discountAmount > 0)
            {
                var discountLine = FormatTotalLine("Discount:", $"-${discountAmount.ToString("F2", CultureInfo.InvariantCulture)}");
                sb.AppendLine(discountLine);
            }
            
            if (adjustment != 0)
            {
                var adjustmentLabel = adjustment > 0 ? "Surcharge:" : "Adjustment:";
                var adjustmentValue = adjustment > 0 ? $"+${Math.Abs(adjustment).ToString("F2", CultureInfo.InvariantCulture)}" : $"${adjustment.ToString("F2", CultureInfo.InvariantCulture)}";
                var adjustmentLine = FormatTotalLine(adjustmentLabel, adjustmentValue);
                sb.AppendLine(adjustmentLine);
            }
            
            var taxLine = FormatTotalLine("Tax:", "$0.00");
            sb.AppendLine(taxLine);
            sb.AppendLine("================================================");
            
            // Total - bold (like Receipt.cshtml) - use full width
            sb.Append(ESC + "E\x01"); // Bold on
            var currencyNotation = order.Currency?.Notation ?? "";
            var totalLine = FormatTotalLine("TOTAL:", $"{currencyNotation}{order.TotalAmount.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine(totalLine);
            sb.Append(ESC + "E\x00"); // Bold off
            
            sb.AppendLine("================================================");
            
            // Payment Info - use full width formatting
            var paymentMethodLine = FormatTotalLine("Payment Method:", "Cash");
            sb.AppendLine(paymentMethodLine);
            
            var amountPaidLine = FormatTotalLine("Amount Paid:", $"${order.TotalAmount.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine(amountPaidLine);
            
            var changeLine = FormatTotalLine("Change:", "$0.00");
            sb.AppendLine(changeLine);
            
            sb.AppendLine("================================================");
            
            // Barcode - like Receipt.cshtml
            sb.Append(ESC + "a\x01"); // Center align
            sb.AppendLine($"*{receiptNumber}*");
            sb.Append(ESC + "a\x00"); // Left align
            
            sb.AppendLine("================================================");
            
            // Footer - using store settings
            sb.Append(ESC + "a\x01"); // Center align
            sb.Append(ESC + "E\x01"); // Bold on
            sb.AppendLine(storeSettings.ReceiptFooterMessage.ToUpperInvariant());
            sb.Append(ESC + "E\x00"); // Bold off
            if (!string.IsNullOrEmpty(storeSettings.FooterLine1))
                sb.AppendLine(storeSettings.FooterLine1);
            if (!string.IsNullOrEmpty(storeSettings.FooterLine2))
                sb.AppendLine(storeSettings.FooterLine2);
            if (!string.IsNullOrEmpty(storeSettings.FooterLine3))
                sb.AppendLine(storeSettings.FooterLine3);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(storeSettings.Website))
                sb.AppendLine($"Questions? Visit us at {storeSettings.Website}");
            if (!string.IsNullOrEmpty(storeSettings.Phone))
                sb.AppendLine($"or call {storeSettings.Phone}");
            sb.Append(ESC + "a\x00"); // Left align
            
            sb.AppendLine();
            sb.AppendLine("================================================");
            
            // Receipt ID for Reference - like Receipt.cshtml
            sb.Append(ESC + "a\x01"); // Center align
            sb.AppendLine($"Order ID: {order.Id} | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
            sb.Append(ESC + "a\x00"); // Left align
            
            // Add generous bottom margin (10 lines) to prevent text bleeding to next page
            // This compensates for the reduced top margin and ensures clean separation
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            
            // Cut paper
            sb.Append(GS + "V\x01");
            
            var receiptText = sb.ToString();
            Console.WriteLine($"[BUILD RECEIPT] Generated receipt text length: {receiptText.Length}");
            Console.WriteLine($"[BUILD RECEIPT] First 300 chars: {receiptText.Substring(0, Math.Min(300, receiptText.Length))}");
            
            // Convert to bytes using Code Page 437 (same as BuildReceipt)
            var bytes = Encoding.GetEncoding(437).GetBytes(receiptText);
            Console.WriteLine($"[BUILD RECEIPT] Generated {bytes.Length} bytes");
            
            return bytes;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BUILD RECEIPT ERROR] {ex.Message}");
            Console.WriteLine($"[BUILD RECEIPT ERROR] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private string FormatTotalLine(string label, string value)
    {
        // Use full thermal printer width (48 characters)
        const int totalWidth = 48;
        var spaces = totalWidth - label.Length - value.Length;
        if (spaces < 1) spaces = 1;
        
        return $"{label}{new string(' ', spaces)}{value}";
    }
}
