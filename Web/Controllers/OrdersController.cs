using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Application.IServices.Sales;
using Application.IServices.Reports;
using Application.IServices.Settings;
using Application.DTOs.Orders;
using Microsoft.AspNetCore.Mvc.Localization;
using Web.Attributes;
using Web.Helpers;
using Web.Services;
using Core.Entities.Sales;
using Core.Enum;
using System.Text;
using static Web.Helpers.CurrencyHelper;

namespace Web.Controllers
{
    [Authorize]
    [RequirePermission(PermissionHelper.Permissions.Orders_View)]
    public class OrdersController : BaseController
    {
        private readonly IOrderService _orderService;
        private readonly IViewLocalizer _localizer;
        private readonly IExportService _exportService;
        private readonly IReceiptService _receiptService;
        private readonly IThermalPrinterService _thermalPrinterService;
        private readonly IStoreSettingsService _storeSettingsService;

        public OrdersController(
            IOrderService orderService,
            IViewLocalizer localizer,
            IExportService exportService,
            IReceiptService receiptService,
            IThermalPrinterService thermalPrinterService,
            IStoreSettingsService storeSettingsService)
        {
            _orderService = orderService;
            _localizer = localizer;
            _exportService = exportService;
            _receiptService = receiptService;
            _thermalPrinterService = thermalPrinterService;
            _storeSettingsService = storeSettingsService;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Orders";
            ViewData["Subtitle"] = "Manage customer orders";

            var recentOrders = await _orderService.GetByDateRangeAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
            ViewBag.RecentOrders = recentOrders.Take(10).ToList();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders(int start = 0, int length = 10, string searchValue = "", string sortColumn = "OrderDate", string sortDirection = "desc")
        {
            try
            {
                var orders = await _orderService.GetByDateRangeAsync(DateTime.UtcNow.AddDays(-365), DateTime.UtcNow);

                if (!string.IsNullOrEmpty(searchValue))
                {
                    orders = orders.Where(o =>
                        o.Id.ToString().Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                        $"{o.Customer?.FirstName} {o.Customer?.LastName}".Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                        o.TotalAmount.ToString().Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                var sortedOrders = sortDirection.ToLower() == "desc"
                    ? orders.OrderByDescending(o => GetOrderPropertyValue(o, sortColumn))
                    : orders.OrderBy(o => GetOrderPropertyValue(o, sortColumn));

                var pagedOrders = sortedOrders.Skip(start).Take(length);

                return Json(new
                {
                    draw = Request.Query["draw"].FirstOrDefault(),
                    recordsTotal = orders.Count,
                    recordsFiltered = orders.Count,
                    data = pagedOrders.Select(o => new
                    {
                        id = o.Id,
                        orderNumber = $"ORD-{o.Id:D6}",
                        customerName = $"{o.Customer?.FirstName} {o.Customer?.LastName}".Trim(),
                        orderDate = o.OrderDate.ToString("yyyy-MM-dd HH:mm"),
                        totalAmount = FormatCurrency(o.TotalAmount, o.Currency?.Notation),
                        status = ((OrderStatus)o.Status).ToString(),
                        statusBadge = GetStatusBadge((OrderStatus)o.Status),
                        itemCount = o.Cart?.CartItems?.Count ?? 0,
                        actions = GetActionButtons(o.Id)
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        private object GetOrderPropertyValue(Order order, string propertyName)
        {
            return propertyName switch
            {
                "OrderDate" => order.OrderDate,
                "TotalAmount" => order.TotalAmount,
                "Status" => order.Status,
                "CustomerName" => $"{order.Customer?.FirstName} {order.Customer?.LastName}".Trim(),
                _ => order.OrderDate
            };
        }

        private string GetStatusBadge(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.Pending => "<span class='badge bg-warning'>Pending</span>",
                OrderStatus.Processing => "<span class='badge bg-info'>Processing</span>",
                OrderStatus.Shipped => "<span class='badge bg-primary'>Shipped</span>",
                OrderStatus.Delivered => "<span class='badge bg-success'>Delivered</span>",
                OrderStatus.Cancelled => "<span class='badge bg-danger'>Cancelled</span>",
                OrderStatus.Returned => "<span class='badge bg-secondary'>Returned</span>",
                _ => "<span class='badge bg-secondary'>Unknown</span>"
            };
        }

        private string GetActionButtons(int orderId)
        {
            return $@"
                <div class='btn-group' role='group'>
                    <button type='button' class='btn btn-sm btn-outline-primary' onclick='viewOrder({orderId})' title='View Details'>
                        <i class='fas fa-eye'></i>
                    </button>
                    <button type='button' class='btn btn-sm btn-outline-success' onclick='printReceipt({orderId})' title='Print Receipt'>
                        <i class='fas fa-receipt'></i>
                    </button>
                    <button type='button' class='btn btn-sm btn-outline-teal' onclick='editOrderStatus({orderId})' title='Update Status'>
                        <i class='fas fa-edit'></i>
                    </button>
                </div>";
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(id);
                if (order == null)
                {
                    TempData["ErrorMessage"] = "Order not found.";
                    return RedirectToAction("Index");
                }

                ViewData["Title"] = "Order Details";
                ViewData["Subtitle"] = $"Order #{order.Id:D6}";

                return View(OrderResponse.FromEntity(order));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Orders_Edit)]
        public async Task<IActionResult> EditStatus(int id)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(id);
                if (order == null)
                {
                    TempData["ErrorMessage"] = "Order not found.";
                    return RedirectToAction("Index");
                }

                ViewData["Title"] = "Update Order Status";
                ViewData["Subtitle"] = $"Order #{order.Id:D6}";

                var model = new UpdateOrderStatusRequest
                {
                    OrderId = order.Id,
                    Status = order.Status
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(PermissionHelper.Permissions.Orders_Edit)]
        public async Task<IActionResult> EditStatus(UpdateOrderStatusRequest model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                await _orderService.UpdateOrderStatusAsync(model.OrderId, model.Status);
                TempData["SuccessMessage"] = "Order status updated successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
                return View(model);
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> CustomerOrders(int customerId)
        {
            try
            {
                var orders = await _orderService.GetByCustomerIdAsync(customerId);

                ViewData["Title"] = "Customer Orders";
                ViewData["Subtitle"] = $"Orders for Customer ID: {customerId}";

                return View(orders.Select(OrderResponse.FromEntity).ToList());
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> ByDateRange(DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.UtcNow.AddDays(-30);
                endDate ??= DateTime.UtcNow;

                var orders = await _orderService.GetByDateRangeAsync(startDate.Value, endDate.Value);

                ViewData["Title"] = "Orders by Date Range";
                ViewData["Subtitle"] = $"Orders from {startDate.Value:yyyy-MM-dd} to {endDate.Value:yyyy-MM-dd}";

                return View(orders.Select(OrderResponse.FromEntity).ToList());
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderStats()
        {
            try
            {
                var startDate = DateTime.UtcNow.AddDays(-30);
                var endDate = DateTime.UtcNow;
                var orders = await _orderService.GetByDateRangeAsync(startDate, endDate);

                var totalOrders = orders.Count;
                var deliveredOrders = orders.Count(o => o.Status == (int)OrderStatus.Delivered);
                var pendingOrders = orders.Count(o => o.Status == (int)OrderStatus.Pending);
                var processingOrders = orders.Count(o => o.Status == (int)OrderStatus.Processing);
                var cancelledOrders = orders.Count(o => o.Status == (int)OrderStatus.Cancelled);
                var totalRevenue = orders.Where(o => o.Status != (int)OrderStatus.Cancelled).Sum(o => o.TotalAmount);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        totalOrders,
                        deliveredOrders,
                        pendingOrders,
                        processingOrders,
                        cancelledOrders,
                        totalRevenue,
                        averageOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Failed to get order stats: {ex.Message}" });
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> GetOrderItems(int orderId)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(orderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found" });
                }

                var items = order.Cart?.CartItems?.Select(ci => new
                {
                    productId = ci.ProductId,
                    productName = ci.Product?.Name ?? "Unknown Product",
                    quantity = ci.Quantity,
                    unitPrice = ci.UnitPrice,
                    totalPrice = ci.TotalPrice,
                    barcode = ci.Product?.Barcode ?? ""
                })?.ToList();

                return Json(new { success = true, data = items });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Failed to get order items: {ex.Message}" });
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> Receipt(int id)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(id);
                if (order == null)
                {
                    TempData["ErrorMessage"] = "Order not found.";
                    return RedirectToAction("Index");
                }

                var storeSettings = await _storeSettingsService.GetStoreSettingsEntityAsync();
                ViewData["StoreSettings"] = storeSettings;
                ViewData["Title"] = "Receipt";
                return View(order);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> ThermalReceipt(int id)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(id);
                if (order == null)
                {
                    TempData["ErrorMessage"] = "Order not found.";
                    return RedirectToAction("Index");
                }

                var storeSettings = await _storeSettingsService.GetStoreSettingsEntityAsync();
                ViewData["StoreSettings"] = storeSettings;
                ViewData["Title"] = "Thermal Receipt";
                ViewData["IsThermalMode"] = true;
                return View("Receipt", order);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> TestMarginAdjustments(int orderId)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(orderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found for margin test" });
                }

                Console.WriteLine($"[MARGIN TEST] Testing margin adjustments for order {orderId}");
                
                // Generate preview to show the margin changes
                var preview = _thermalPrinterService.PreviewReceiptContent(order);
                
                // Test print with adjusted margins
                var printed = await _thermalPrinterService.PrintDirectReceiptAsync(order);
                
                return Json(new { 
                    success = printed, 
                    message = printed 
                        ? "✅ Margin adjustments test successful - reduced top margin, increased bottom margin" 
                        : "❌ Margin adjustments test failed",
                    orderId = orderId,
                    preview = preview,
                    marginChanges = new[] {
                        "✅ Reduced top margin - receipt starts immediately after init",
                        "✅ Increased bottom margin - 10 blank lines before cut",
                        "✅ Reduced spacing between items to compensate",
                        "✅ Better balance between top and bottom spacing"
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MARGIN TEST ERROR] {ex}");
                return Json(new { success = false, message = $"Margin test error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> TestFullWidthFormatting(int orderId)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(orderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found for formatting test" });
                }

                Console.WriteLine($"[FULL WIDTH TEST] Testing full-width formatting for order {orderId}");
                
                // Generate preview to show the formatting
                var preview = _thermalPrinterService.PreviewReceiptContent(order);
                
                // Test print with full-width formatting
                var printed = await _thermalPrinterService.PrintDirectReceiptAsync(order);
                
                return Json(new { 
                    success = printed, 
                    message = printed 
                        ? "✅ Full-width formatting test successful - totals use full page width, added bottom margin" 
                        : "❌ Full-width formatting test failed",
                    orderId = orderId,
                    preview = preview,
                    improvements = new[] {
                        "✅ Totals now use full 48-character width",
                        "✅ Added 5 blank lines at bottom to prevent bleeding",
                        "✅ Consistent separator lines (48 characters)",
                        "✅ Full-width formatting for all receipt sections"
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FULL WIDTH TEST ERROR] {ex}");
                return Json(new { success = false, message = $"Full-width formatting test error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> PreviewThermalReceipt(int orderId)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(orderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found for preview" });
                }

                Console.WriteLine($"[RECEIPT PREVIEW] Generating preview for order {orderId}");
                
                var preview = _thermalPrinterService.PreviewReceiptContent(order);
                
                return Json(new { 
                    success = true, 
                    message = "Receipt preview generated successfully",
                    orderId = orderId,
                    preview = preview,
                    orderInfo = new {
                        id = order.Id,
                        total = order.TotalAmount,
                        date = order.OrderDate.ToString("yyyy-MM-dd HH:mm"),
                        itemsCount = order.Cart?.CartItems?.Count ?? 0
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RECEIPT PREVIEW ERROR] {ex}");
                return Json(new { success = false, message = $"Preview error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> TestFullReceiptFormat(int orderId)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(orderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found for full receipt test" });
                }

                Console.WriteLine($"[FULL RECEIPT TEST] Testing complete receipt format for order {orderId}");
                Console.WriteLine($"[FULL RECEIPT TEST] Order total: ${order.TotalAmount:F2}");
                Console.WriteLine($"[FULL RECEIPT TEST] Items count: {order.Cart?.CartItems?.Count ?? 0}");
                
                // Test the exact same method used in checkout
                var printed = await _thermalPrinterService.PrintDirectReceiptAsync(order);
                
                var message = printed 
                    ? "✅ Full receipt format test successful - matches Receipt.cshtml layout!" 
                    : "❌ Full receipt format test failed - check printer and console logs";
                
                Console.WriteLine($"[FULL RECEIPT TEST] Result: {message}");
                
                return Json(new { 
                    success = printed, 
                    message = message,
                    orderId = orderId,
                    orderTotal = order.TotalAmount,
                    itemsCount = order.Cart?.CartItems?.Count ?? 0
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FULL RECEIPT TEST ERROR] {ex}");
                return Json(new { success = false, message = $"Full receipt test error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> TestSimplePrint()
        {
            try
            {
                Console.WriteLine($"[SIMPLE PRINT TEST] Starting simple print test...");
                
                // Create a very simple test receipt
                var testText = @"
        TEST RECEIPT
    ==================
    This is a test print
    Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + @"
    ==================
    If you can read this,
    your printer is working!
    ==================
";

                // Convert to ESC/POS bytes
                var escPosText = "\x1B@" + testText + "\x1D\x56\x01"; // Init + text + cut
                var bytes = System.Text.Encoding.GetEncoding(437).GetBytes(escPosText);
                
                Console.WriteLine($"[SIMPLE PRINT TEST] Generated {bytes.Length} test bytes");
                
                // Try to print using thermal printer service
                var printed = await _thermalPrinterService.PrintTextAsync(testText);
                
                return Json(new { 
                    success = printed, 
                    message = printed ? "✅ Simple test print successful!" : "❌ Simple test print failed",
                    testText = testText
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SIMPLE PRINT TEST ERROR] {ex}");
                return Json(new { success = false, message = $"Simple print test error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> CompleteThermalDiagnostic()
        {
            try
            {
                Console.WriteLine($"[THERMAL DIAGNOSTIC] Starting complete thermal printing diagnostic...");
                
                var diagnosticResults = new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    printerTests = new List<object>(),
                    systemInfo = new
                    {
                        installedPrinters = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList(),
                        defaultSystemPrinter = new System.Drawing.Printing.PrinterSettings().PrinterName
                    }
                };

                // Test 1: Check default thermal printer
                var defaultPrinter = "POSPrinter POS80";
                var defaultAvailable = _thermalPrinterService.IsPrinterAvailable(defaultPrinter);
                Console.WriteLine($"[THERMAL DIAGNOSTIC] Default printer '{defaultPrinter}': {(defaultAvailable ? "✅ Available" : "❌ Not Available")}");

                // Test 2: Check all installed printers
                foreach (var printer in diagnosticResults.systemInfo.installedPrinters)
                {
                    var available = _thermalPrinterService.IsPrinterAvailable(printer);
                    Console.WriteLine($"[THERMAL DIAGNOSTIC] Printer '{printer}': {(available ? "✅ Available" : "❌ Not Available")}");
                }

                // Test 3: Try simple text print to available printer
                var testPrintResult = false;
                var testPrinterUsed = "";
                
                if (defaultAvailable)
                {
                    testPrinterUsed = defaultPrinter;
                    var testText = "THERMAL PRINTER TEST\n" + DateTime.Now.ToString() + "\nTest successful!\n";
                    testPrintResult = await _thermalPrinterService.PrintTextAsync(testText, defaultPrinter);
                }
                else
                {
                    // Try first available printer
                    foreach (var printer in diagnosticResults.systemInfo.installedPrinters)
                    {
                        if (_thermalPrinterService.IsPrinterAvailable(printer))
                        {
                            testPrinterUsed = printer;
                            var testText = "THERMAL PRINTER TEST\n" + DateTime.Now.ToString() + "\nTest successful!\n";
                            testPrintResult = await _thermalPrinterService.PrintTextAsync(testText, printer);
                            break;
                        }
                    }
                }

                Console.WriteLine($"[THERMAL DIAGNOSTIC] Test print result: {(testPrintResult ? "✅ SUCCESS" : "❌ FAILED")}");
                Console.WriteLine($"[THERMAL DIAGNOSTIC] Test printer used: {testPrinterUsed}");

                return Json(new { 
                    success = true,
                    diagnostic = diagnosticResults,
                    defaultPrinterAvailable = defaultAvailable,
                    testPrintResult = testPrintResult,
                    testPrinterUsed = testPrinterUsed,
                    message = $"Thermal diagnostic completed. Default printer available: {defaultAvailable}, Test print: {(testPrintResult ? "SUCCESS" : "FAILED")}"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[THERMAL DIAGNOSTIC ERROR] {ex}");
                return Json(new { success = false, message = $"Thermal diagnostic error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> TestPrinterConnection()
        {
            try
            {
                Console.WriteLine($"[PRINTER TEST] Testing printer connection...");
                
                var defaultPrinter = "POSPrinter POS80";
                var isAvailable = _thermalPrinterService.IsPrinterAvailable(defaultPrinter);
                
                Console.WriteLine($"[PRINTER TEST] Default printer '{defaultPrinter}' available: {isAvailable}");
                
                // List all available printers
                var installedPrinters = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList();
                
                Console.WriteLine($"[PRINTER TEST] Found {installedPrinters.Count} installed printers:");
                foreach (var printer in installedPrinters)
                {
                    var available = _thermalPrinterService.IsPrinterAvailable(printer);
                    Console.WriteLine($"[PRINTER TEST] - {printer}: {(available ? "✅ Available" : "❌ Not Available")}");
                }
                
                return Json(new { 
                    success = true, 
                    defaultPrinterAvailable = isAvailable,
                    defaultPrinter = defaultPrinter,
                    installedPrinters = installedPrinters,
                    message = $"Printer test completed. Default printer available: {isAvailable}"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PRINTER TEST ERROR] {ex}");
                return Json(new { success = false, message = $"Printer test error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> TestDirectPrint(int orderId)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(orderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found" });
                }

                Console.WriteLine($"[DIRECT PRINT TEST] Starting direct ESC/POS print test for order {orderId}...");
                Console.WriteLine($"[DIRECT PRINT TEST] Order has {order.Cart?.CartItems?.Count ?? 0} items");
                
                var printed = await _thermalPrinterService.PrintDirectReceiptAsync(order);
                
                var message = printed 
                    ? "✅ Direct ESC/POS receipt printed successfully - NO HTML, NO white lines!" 
                    : "❌ Direct print failed - check printer connection and name";
                
                Console.WriteLine($"[DIRECT PRINT TEST] Result: {message}");
                
                return Json(new { 
                    success = printed, 
                    message = message
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIRECT PRINT TEST ERROR] {ex}");
                return Json(new { success = false, message = $"Direct print test error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> TestPureTextPrint(int orderId)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(orderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found" });
                }

                Console.WriteLine($"[PURE TEXT PRINT] Starting pure text print test...");
                
                var textReceipt = GenerateTextReceipt(order);
                var printed = await _thermalPrinterService.PrintTextAsync(textReceipt);
                
                return Json(new { 
                    success = printed, 
                    message = printed ? "Pure text receipt printed successfully - no HTML, no white lines!" : "Pure text print failed",
                    text = textReceipt
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PURE TEXT PRINT ERROR] {ex}");
                return Json(new { success = false, message = $"Pure text print error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> PrintReceipt(int id)
        {
            try
            {
                var order = await _orderService.GetByIdAsync(id);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found" });
                }

                // Reprint of an existing order: straight to the receipt printer, no dialog. Uses the
                // order's stored receipt (or one built from the order) - never creates a new row.
                var document = await _thermalPrinterService.GetOrderReceiptDocumentAsync(order);
                return await PrintToReceiptPrinterAsync(_thermalPrinterService, document,
                    Url.Action(nameof(ReceiptPdf), "Orders", new { id }));
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> ReceiptPdf(int id)
        {
            var order = await _orderService.GetByIdAsync(id);
            if (order == null)
                return NotFound();

            var document = await _thermalPrinterService.GetOrderReceiptDocumentAsync(order);
            return PdfFile(_thermalPrinterService, document);
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Orders_View)]
        public async Task<IActionResult> PrintReceiptForCheckout(int orderId)
        {
            try
            {
                Console.WriteLine($"[THERMAL PRINT] Starting checkout print for order ID: {orderId}");
                
                if (orderId <= 0)
                {
                    Console.WriteLine($"[THERMAL PRINT ERROR] Invalid order ID: {orderId}");
                    return Json(new { success = false, message = "Invalid order ID" });
                }

                var order = await _orderService.GetByIdAsync(orderId);
                if (order == null)
                {
                    Console.WriteLine($"[THERMAL PRINT ERROR] Order {orderId} not found in database");
                    return Json(new { success = false, message = "Order not found" });
                }

                Console.WriteLine($"[THERMAL PRINT] Order found: ID={order.Id}, Total=${order.TotalAmount:F2}");
                Console.WriteLine($"[THERMAL PRINT] Cart is null: {order.Cart == null}");
                Console.WriteLine($"[THERMAL PRINT] Cart items count: {order.Cart?.CartItems?.Count ?? 0}");

                // Create pure ESC/POS receipt directly - completely bypass HTML
                Console.WriteLine($"[THERMAL PRINT] Calling PrintDirectReceiptAsync...");
                
                var printed = await _thermalPrinterService.PrintDirectReceiptAsync(order);
                
                Console.WriteLine($"[THERMAL PRINT] PrintDirectReceiptAsync returned: {printed}");
                
                if (printed)
                {
                    Console.WriteLine($"[THERMAL PRINT] ✅ Direct receipt printed successfully");
                    return Json(new { 
                        success = true, 
                        message = "Receipt printed successfully using direct ESC/POS commands - matches Receipt.cshtml layout",
                        receiptUrl = $"/Orders/Receipt/{orderId}",
                        printMethod = "Direct ESC/POS",
                        orderInfo = new {
                            id = order.Id,
                            total = order.TotalAmount,
                            itemsCount = order.Cart?.CartItems?.Count ?? 0
                        }
                    });
                }

                Console.WriteLine($"[THERMAL PRINT] ❌ Direct ESC/POS printing failed");
                
                // Enhanced error message with troubleshooting info
                var errorMessage = "Direct thermal printing failed. ";
                
                // Check if any printers are available
                var installedPrinters = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList();
                var availablePrinters = installedPrinters.Where(p => _thermalPrinterService.IsPrinterAvailable(p)).ToList();
                
                if (!availablePrinters.Any())
                {
                    errorMessage += "No printers are available. Check printer connections.";
                }
                else
                {
                    errorMessage += $"Available printers: {string.Join(", ", availablePrinters)}. Check printer name 'POSPrinter POS80'.";
                }

                return Json(new { 
                    success = false, 
                    message = errorMessage,
                    receiptUrl = $"/Orders/Receipt/{orderId}",
                    troubleshooting = new {
                        installedPrinters = installedPrinters,
                        availablePrinters = availablePrinters,
                        defaultPrinter = "POSPrinter POS80",
                        suggestion = "Use /Orders/CompleteThermalDiagnostic to test printer setup"
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[THERMAL PRINT ERROR] Exception: {ex.Message}");
                Console.WriteLine($"[THERMAL PRINT ERROR] Stack trace: {ex.StackTrace}");
                return Json(new { success = false, message = $"Error printing receipt: {ex.Message}" });
            }
        }

        private string GenerateTextReceipt(Order order)
        {
            var receipt = new StringBuilder();
            const string line = "--------------------------------";
            
            // Header
            receipt.AppendLine("        YOUR STORE NAME");
            receipt.AppendLine("       123 Main Street");
            receipt.AppendLine("      City, State 12345");
            receipt.AppendLine("     Phone: (123) 456-7890");
            receipt.AppendLine(line);
            
            // Receipt info
            receipt.AppendLine($"Receipt: ORD-{order.Id:D6}");
            receipt.AppendLine($"Date: {order.OrderDate:yyyy-MM-dd HH:mm}");
            receipt.AppendLine($"Customer: {order.Customer?.FirstName} {order.Customer?.LastName}".Trim());
            receipt.AppendLine($"Cashier: {User.Identity?.Name ?? "System"}");
            receipt.AppendLine(line);
            
            // Items
            if (order.Cart?.CartItems != null && order.Cart.CartItems.Any())
            {
                foreach (var item in order.Cart.CartItems)
                {
                    var name = item.Product?.Name ?? "Item";
                    if (name.Length > 25) name = name.Substring(0, 22) + "...";
                    
                    receipt.AppendLine(name);
                    
                    var qtyPrice = $"{item.Quantity} x ${item.UnitPrice:F2}";
                    var total = $"${item.TotalPrice:F2}";
                    var spaces = 32 - qtyPrice.Length - total.Length;
                    if (spaces < 1) spaces = 1;
                    
                    receipt.AppendLine($"{qtyPrice}{new string(' ', spaces)}{total}");
                }
            }
            
            receipt.AppendLine(line);
            
            // Totals
            var subtotal = order.Cart?.CartItems?.Sum(i => i.TotalPrice) ?? order.TotalAmount;
            var discount = subtotal - order.TotalAmount;
            
            if (discount > 0)
            {
                receipt.AppendLine($"Subtotal:                ${subtotal:F2}");
                receipt.AppendLine($"Discount:               -${discount:F2}");
            }
            
            receipt.AppendLine($"Tax:                     $0.00");
            receipt.AppendLine(line);
            receipt.AppendLine($"TOTAL:                  ${order.TotalAmount:F2}");
            receipt.AppendLine(line);
            
            // Payment info
            receipt.AppendLine($"Payment Method:           Cash");
            receipt.AppendLine($"Amount Paid:            ${order.TotalAmount:F2}");
            receipt.AppendLine($"Change:                  $0.00");
            receipt.AppendLine(line);
            
            // Footer
            receipt.AppendLine("      THANK YOU!");
            receipt.AppendLine("   Please keep this receipt");
            receipt.AppendLine("  Returns accepted within 30 days");
            receipt.AppendLine();
            receipt.AppendLine($"    Order ID: {order.Id}");
            receipt.AppendLine($"  {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            
            return receipt.ToString();
        }

        // [HttpGet]
        // public IActionResult CheckPrinter()
        // {
        //     var printerName = _thermalPrinterService.GetDefaultPrinterName();
        //     var isAvailable = _thermalPrinterService.IsPrinterAvailable();
        //
        //     return Json(new { success = true, printerName, isAvailable });
        // }

        [HttpPost]
        public async Task<IActionResult> ExportOrders()
        {
            try
            {
                var csvData = await _exportService.ExportOrdersToCsvAsync();
                var fileName = $"orders_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

                return File(csvData, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }
    }
}

