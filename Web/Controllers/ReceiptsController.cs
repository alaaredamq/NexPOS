using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Web.Attributes;
using Web.Helpers;
using Application.IServices.Sales;
using Core.Entities.Sales;
using static Web.Helpers.CurrencyHelper;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Sales_View)]
public class ReceiptsController : BaseController
{
    private readonly IReceiptService _receiptService;

    public ReceiptsController(IReceiptService receiptService)
    {
        _receiptService = receiptService;
    }

    public IActionResult Index()
    {
        ViewData["Title"] = "Receipt Management";
        ViewData["Subtitle"] = "Manage and view sales receipts";
        return View();
    }

    [HttpGet("test-connection")]
    public async Task<IActionResult> TestConnection()
    {
        try
        {
            // Test if we can query the receipts table
            var count = await _receiptService.GetAllAsync();
            return Json(new { 
                success = true, 
                message = "Database connection successful", 
                receiptCount = count?.Count ?? 0,
                tablesExist = true
            });
        }
        catch (Exception ex)
        {
            return Json(new { 
                success = false, 
                error = ex.Message,
                innerError = ex.InnerException?.Message,
                stackTrace = ex.StackTrace,
                tablesExist = false
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var receipt = await _receiptService.GetReceiptAsync(id);
            if (receipt == null)
            {
                TempData["ErrorMessage"] = "Receipt not found.";
                return RedirectToAction("Index");
            }
            
            ViewData["Title"] = "Receipt Details";
            ViewData["Subtitle"] = $"Receipt #{receipt.ReceiptNumber}";
            
            return View(receipt);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error loading receipt: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string term)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return Json(new List<object>());
            }

            var receipts = await _receiptService.SearchReceiptsAsync(term);
            return Json(receipts.Select(r => new
            {
                id = r.Id,
                receiptNumber = r.ReceiptNumber ?? "",
                customerName = !string.IsNullOrEmpty(r.CustomerName) 
                    ? r.CustomerName 
                    : (r.Customer != null ? $"{r.Customer.FirstName ?? ""} {r.Customer.LastName ?? ""}".Trim() : "Walk-in"),
                totalAmount = r.TotalAmount,
                receiptDate = r.ReceiptDate.ToString("yyyy-MM-dd HH:mm"),
                cashierName = r.Cashier != null 
                    ? $"{r.Cashier.FirstName ?? ""} {r.Cashier.LastName ?? ""}".Trim() 
                    : "N/A",
                paymentMethod = r.PaymentMethod?.Name ?? "Cash",
                isVoid = r.IsVoid
            }));
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message, details = ex.ToString() });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStats(DateTime? startDate, DateTime? endDate)
    {
        try
        {
            var stats = await _receiptService.GetReceiptStatsAsync(startDate, endDate);
            return Json(stats);
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetReceipts(int start = 0, int length = 10, string searchValue = "", string sortColumn = "ReceiptDate", string sortDirection = "desc")
    {
        try
        {
            var receipts = await _receiptService.GetRecentReceiptsAsync(1000);
            
            // If no receipts, return empty data
            if (receipts == null || !receipts.Any())
            {
                return Json(new
                {
                    draw = Request.Query["draw"].FirstOrDefault(),
                    recordsTotal = 0,
                    recordsFiltered = 0,
                    data = new List<object>()
                });
            }
            
            // Apply search filter
            if (!string.IsNullOrEmpty(searchValue))
            {
                receipts = receipts.Where(r => 
                    (r.ReceiptNumber ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (r.CustomerName ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    r.TotalAmount.ToString().Contains(searchValue)
                ).ToList();
            }

            var sortedReceipts = sortDirection.ToLower() == "desc" 
                ? receipts.OrderByDescending(r => r.ReceiptDate)
                : receipts.OrderBy(r => r.ReceiptDate);

            var pagedReceipts = sortedReceipts.Skip(start).Take(length).ToList();

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = receipts.Count,
                recordsFiltered = receipts.Count,
                data = pagedReceipts.Select(r => new
                {
                    id = r.Id,
                    receiptNumber = r.ReceiptNumber ?? "",
                    customerName = !string.IsNullOrEmpty(r.CustomerName) 
                        ? r.CustomerName 
                        : (r.Customer != null ? $"{r.Customer.FirstName ?? ""} {r.Customer.LastName ?? ""}".Trim() : "Walk-in"),
                    cashierName = r.Cashier != null 
                        ? $"{r.Cashier.FirstName ?? ""} {r.Cashier.LastName ?? ""}".Trim() 
                        : "N/A",
                    receiptDate = r.ReceiptDate.ToString("yyyy-MM-dd HH:mm"),
                    totalAmount = FormatCurrency(r.TotalAmount, r.Currency?.Notation),
                    paymentMethod = r.PaymentMethod?.Name ?? "Cash",
                    statusBadge = r.IsVoid ? "<span class='badge bg-danger'>Void</span>" : "<span class='badge bg-success'>Valid</span>",
                    actions = GetActionButtons(r.Id, r.IsVoid)
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error loading receipts: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMessage += $" | Inner: {ex.InnerException.Message}";
            }
            
            // Log the full error for debugging
            Console.WriteLine($"Receipt Error: {ex}");
            
            return Json(new { 
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = 0,
                recordsFiltered = 0,
                data = new List<object>(),
                error = errorMessage
            });
        }
    }

    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentReceipts(int start = 0, int length = 10, string searchValue = "", string sortColumn = "ReceiptDate", string sortDirection = "desc")
    {
        try
        {
            var receipts = await _receiptService.GetRecentReceiptsAsync(50);
            
            // If no receipts, return empty data
            if (receipts == null || !receipts.Any())
            {
                return Json(new
                {
                    draw = Request.Query["draw"].FirstOrDefault(),
                    recordsTotal = 0,
                    recordsFiltered = 0,
                    data = new List<object>()
                });
            }
            
            // Apply search filter
            if (!string.IsNullOrEmpty(searchValue))
            {
                receipts = receipts.Where(r => 
                    (r.ReceiptNumber ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (r.CustomerName ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            var pagedReceipts = receipts.Skip(start).Take(length).ToList();

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = receipts.Count,
                recordsFiltered = receipts.Count,
                data = pagedReceipts.Select(r => new
                {
                    id = r.Id,
                    receiptNumber = r.ReceiptNumber ?? "",
                    customerName = !string.IsNullOrEmpty(r.CustomerName) 
                        ? r.CustomerName 
                        : (r.Customer != null ? $"{r.Customer.FirstName ?? ""} {r.Customer.LastName ?? ""}".Trim() : "Walk-in"),
                    totalAmount = FormatCurrency(r.TotalAmount, r.Currency?.Notation),
                    receiptDate = r.ReceiptDate.ToString("yyyy-MM-dd HH:mm"),
                    actions = GetActionButtons(r.Id, r.IsVoid)
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error loading recent receipts: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMessage += $" | Inner: {ex.InnerException.Message}";
            }
            
            Console.WriteLine($"Recent Receipts Error: {ex}");
            
            return Json(new { 
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = 0,
                recordsFiltered = 0,
                data = new List<object>(),
                error = errorMessage
            });
        }
    }

    [HttpGet("void")]
    public async Task<IActionResult> GetVoidReceipts(int start = 0, int length = 10, string searchValue = "", string sortColumn = "ReceiptDate", string sortDirection = "desc")
    {
        try
        {
            var allReceipts = await _receiptService.GetRecentReceiptsAsync(1000);
            var voidReceipts = allReceipts?.Where(r => r.IsVoid).ToList() ?? new List<Receipt>();
            
            // If no void receipts, return empty data
            if (!voidReceipts.Any())
            {
                return Json(new
                {
                    draw = Request.Query["draw"].FirstOrDefault(),
                    recordsTotal = 0,
                    recordsFiltered = 0,
                    data = new List<object>()
                });
            }
            
            // Apply search filter
            if (!string.IsNullOrEmpty(searchValue))
            {
                voidReceipts = voidReceipts.Where(r => 
                    (r.ReceiptNumber ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (r.CustomerName ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            var sortedReceipts = sortDirection.ToLower() == "desc" 
                ? voidReceipts.OrderByDescending(r => r.VoidDate ?? r.ReceiptDate)
                : voidReceipts.OrderBy(r => r.VoidDate ?? r.ReceiptDate);

            var pagedReceipts = sortedReceipts.Skip(start).Take(length).ToList();

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = voidReceipts.Count,
                recordsFiltered = voidReceipts.Count,
                data = pagedReceipts.Select(r => new
                {
                    id = r.Id,
                    receiptNumber = r.ReceiptNumber ?? "",
                    customerName = !string.IsNullOrEmpty(r.CustomerName) 
                        ? r.CustomerName 
                        : (r.Customer != null ? $"{r.Customer.FirstName ?? ""} {r.Customer.LastName ?? ""}".Trim() : "Walk-in"),
                    totalAmount = FormatCurrency(r.TotalAmount, r.Currency?.Notation),
                    voidDate = r.VoidDate.HasValue ? r.VoidDate.Value.ToString("yyyy-MM-dd HH:mm") : r.ReceiptDate.ToString("yyyy-MM-dd HH:mm"),
                    voidReason = r.VoidReason ?? "N/A",
                    voidedBy = r.VoidedByUser != null 
                        ? $"{r.VoidedByUser.FirstName ?? ""} {r.VoidedByUser.LastName ?? ""}".Trim() 
                        : (r.Cashier != null ? $"{r.Cashier.FirstName ?? ""} {r.Cashier.LastName ?? ""}".Trim() : "N/A"),
                    actions = "<button class='btn btn-sm btn-outline-primary' onclick='viewReceipt(" + r.Id + ")'><i class='fas fa-eye'></i></button>"
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error loading void receipts: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMessage += $" | Inner: {ex.InnerException.Message}";
            }
            
            Console.WriteLine($"Void Receipts Error: {ex}");
            
            return Json(new { 
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = 0,
                recordsFiltered = 0,
                data = new List<object>(),
                error = errorMessage
            });
        }
    }

    private string GetActionButtons(int receiptId, bool isVoid)
    {
        var buttons = $@"
            <div class='btn-group' role='group'>
                <button type='button' class='btn btn-sm btn-outline-primary' onclick='viewReceipt({receiptId})' title='View'>
                    <i class='fas fa-eye'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-success' onclick='downloadPdf({receiptId})' title='Download PDF'>
                    <i class='fas fa-download'></i>
                </button>";
        
        if (!isVoid)
        {
            buttons += $@"
                <button type='button' class='btn btn-sm btn-outline-danger' onclick='voidReceipt({receiptId})' title='Void'>
                    <i class='fas fa-ban'></i>
                </button>";
        }
        
        buttons += "</div>";
        return buttons;
    }

    [HttpGet("by-date-range")]
    public async Task<IActionResult> GetByDateRange(DateTime startDate, DateTime endDate)
    {
        try
        {
            var receipts = await _receiptService.GetReceiptsByDateRangeAsync(startDate, endDate);
            return Json(receipts.Select(r => new
            {
                id = r.Id,
                receiptNumber = r.ReceiptNumber ?? "",
                customerName = !string.IsNullOrEmpty(r.CustomerName) 
                    ? r.CustomerName 
                    : (r.Customer != null ? $"{r.Customer.FirstName ?? ""} {r.Customer.LastName ?? ""}".Trim() : "Walk-in"),
                totalAmount = r.TotalAmount,
                receiptDate = r.ReceiptDate.ToString("yyyy-MM-dd HH:mm"),
                cashierName = r.Cashier != null 
                    ? $"{r.Cashier.FirstName ?? ""} {r.Cashier.LastName ?? ""}".Trim() 
                    : "N/A",
                paymentMethod = r.PaymentMethod?.Name ?? "Cash",
                isVoid = r.IsVoid
            }));
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message, details = ex.ToString() });
        }
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Sales_Edit)]
    public async Task<IActionResult> VoidReceipt(int id, [FromBody] VoidReceiptRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var receipt = await _receiptService.VoidReceiptAsync(id, userId, request.Reason);
            return Json(new { success = true, message = "Receipt voided successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> DownloadPdf(int id)
    {
        try
        {
            var htmlBytes = await _receiptService.GenerateReceiptPdfAsync(id);
            var receipt = await _receiptService.GetReceiptAsync(id);
            
            if (receipt == null)
            {
                TempData["ErrorMessage"] = "Receipt not found.";
                return RedirectToAction("Index");
            }
            
            // For now, returning HTML. In production, convert to PDF using a library
            return File(htmlBytes, "text/html", $"Receipt-{receipt.ReceiptNumber}.html");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error generating receipt: {ex.Message}";
            return RedirectToAction("Details", new { id });
        }
    }

    [HttpGet]
    public async Task<IActionResult> PrintReceipt(int id)
    {
        try
        {
            var htmlBytes = await _receiptService.GenerateReceiptPdfAsync(id);
            var html = System.Text.Encoding.UTF8.GetString(htmlBytes);
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error generating receipt: {ex.Message}";
            return RedirectToAction("Details", new { id });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SendEmail(int id, [FromBody] EmailReceiptRequest request)
    {
        try
        {
            await _receiptService.SendReceiptEmailAsync(id, request.EmailAddress);
            return Json(new { success = true, message = "Receipt sent successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("generate-number")]
    public async Task<IActionResult> GenerateReceiptNumber()
    {
        try
        {
            var receiptNumber = await _receiptService.GenerateReceiptNumberAsync();
            return Json(new { receiptNumber });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
        {
            return userId;
        }
        
        // Fallback: try to get from "UserId" claim
        var userIdClaim2 = User.FindFirst("UserId");
        if (userIdClaim2 != null && int.TryParse(userIdClaim2.Value, out int userId2))
        {
            return userId2;
        }
        
        throw new UnauthorizedAccessException("User ID not found in claims");
    }
}

public record VoidReceiptRequest(string Reason);
public record EmailReceiptRequest(string EmailAddress);

