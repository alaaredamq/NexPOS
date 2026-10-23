using Application.IServices.Sales;
using Application.Services.Printing;
using Core.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Sales_View)]
public class CashDrawerController : BaseController
{
    private readonly ICashDrawerService _cashDrawerService;
    private readonly IThermalPrinterService _thermalPrinterService;
    private readonly ILogger<CashDrawerController> _logger;

    public CashDrawerController(
        ICashDrawerService cashDrawerService,
        IThermalPrinterService thermalPrinterService,
        ILogger<CashDrawerController> logger)
    {
        _cashDrawerService = cashDrawerService;
        _thermalPrinterService = thermalPrinterService;
        _logger = logger;
    }

    // Main cash drawer management page
    public async Task<IActionResult> Index()
    {
        var userId = GetCurrentUserId();
        var openSession = await _cashDrawerService.GetOpenSessionByUserIdAsync(userId);
        
        ViewData["Title"] = "Cash Drawer Management";
        ViewData["HasOpenSession"] = openSession != null;
        ViewData["OpenSessionId"] = openSession?.Id;
        
        return View();
    }

    // Open drawer page
    [HttpGet]
    public IActionResult Open()
    {
        ViewData["Title"] = "Open Cash Drawer";
        return View();
    }

    // Open drawer API
    [HttpPost]
    public async Task<IActionResult> OpenDrawer([FromBody] OpenDrawerRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            // Check if user already has open session
            if (await _cashDrawerService.HasOpenSessionAsync(userId))
            {
                return Json(new { success = false, message = "You already have an open cash drawer session" });
            }

            var session = await _cashDrawerService.OpenDrawerAsync(userId, request.OpeningCash, request.Notes);
            
            return Json(new { 
                success = true, 
                message = "Cash drawer opened successfully",
                data = new {
                    sessionId = session.Id,
                    openedAt = session.OpenedAt,
                    openingCash = session.OpeningCash
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening cash drawer");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // Get current open session
    [HttpGet]
    public async Task<IActionResult> GetOpenSession()
    {
        try
        {
            var userId = GetCurrentUserId();
            var session = await _cashDrawerService.GetOpenSessionByUserIdAsync(userId);
            
            if (session == null)
            {
                return Json(new { success = false, message = "No open session found" });
            }

            var summary = await _cashDrawerService.GetSessionSummaryAsync(session.Id);
            
            return Json(new { 
                success = true, 
                data = new {
                    sessionId = session.Id,
                    openedAt = session.OpenedAt,
                    openingCash = session.OpeningCash,
                    summary = summary
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting open session");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // Close drawer page
    [HttpGet]
    public async Task<IActionResult> Close(int? id)
    {
        var userId = GetCurrentUserId();
        var session = id.HasValue 
            ? await _cashDrawerService.GetSessionWithDetailsAsync(id.Value)
            : await _cashDrawerService.GetOpenSessionByUserIdAsync(userId);
        
        if (session == null)
        {
            TempData["ErrorMessage"] = "No open cash drawer session found";
            return RedirectToAction("Index");
        }

        var summary = await _cashDrawerService.GetSessionSummaryAsync(session.Id);
        ViewData["Title"] = "Close Cash Drawer";
        ViewData["SessionSummary"] = summary;
        
        return View(session);
    }

    // Close drawer API
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Sales_Edit)]
    public async Task<IActionResult> CloseDrawer([FromBody] CloseDrawerRequest request)
    {
        try
        {
            var session = await _cashDrawerService.CloseDrawerAsync(
                request.SessionId, 
                request.ActualCash, 
                request.Notes);
            
            return Json(new { 
                success = true, 
                message = "Cash drawer closed successfully",
                data = new {
                    sessionId = session.Id,
                    closedAt = session.ClosedAt,
                    expectedCash = session.ExpectedCash,
                    actualCash = session.ActualCash,
                    difference = session.CashDifference
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing cash drawer");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // Add cash drop
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Sales_Edit)]
    public async Task<IActionResult> AddCashDrop([FromBody] CashDropRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var cashDrop = await _cashDrawerService.AddCashDropAsync(
                request.SessionId, 
                userId, 
                request.Amount, 
                request.Reason, 
                request.Notes);
            
            return Json(new { 
                success = true, 
                message = "Cash drop recorded successfully",
                data = new {
                    id = cashDrop.Id,
                    amount = cashDrop.Amount,
                    droppedAt = cashDrop.DroppedAt
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding cash drop");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // Get session summary
    [HttpGet]
    public async Task<IActionResult> GetSessionSummary(int sessionId)
    {
        try
        {
            var summary = await _cashDrawerService.GetSessionSummaryAsync(sessionId);
            return Json(new { success = true, data = summary });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session summary");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // Session history
    [HttpGet]
    public async Task<IActionResult> History()
    {
        ViewData["Title"] = "Cash Drawer History";
        return View();
    }

    // Get session history API
    [HttpGet]
    public async Task<IActionResult> GetHistory(DateTime? startDate, DateTime? endDate)
    {
        try
        {
            var userId = GetCurrentUserId();

            // The date picker sends midnight; make the end date inclusive of the whole day
            if (endDate.HasValue && endDate.Value.TimeOfDay == TimeSpan.Zero)
            {
                endDate = endDate.Value.Date.AddDays(1).AddTicks(-1);
            }

            var sessions = await _cashDrawerService.GetSessionHistoryAsync(userId, startDate, endDate);

            var data = sessions.Select(s => new {
                id = s.Id,
                openedAt = s.OpenedAt,
                closedAt = s.ClosedAt,
                openingCash = s.OpeningCash,
                closingCash = s.ActualCash,
                totalSales = s.TotalSales,
                totalTransactions = s.TotalTransactions,
                cashDifference = s.CashDifference,
                isOpen = s.IsOpen
            });
            
            return Json(new { success = true, data = data });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session history");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // Z-Report page
    [HttpGet]
    public async Task<IActionResult> ZReport(int id)
    {
        try
        {
            var session = await _cashDrawerService.GetSessionWithDetailsAsync(id);
            if (session == null)
            {
                TempData["ErrorMessage"] = "Session not found";
                return RedirectToAction("History");
            }

            if (session.IsOpen)
            {
                TempData["ErrorMessage"] = "Cannot generate Z-Report for an open session";
                return RedirectToAction("Close", new { id });
            }

            var report = await _cashDrawerService.GenerateZReportAsync(id);
            ViewData["Title"] = "Z-Report";
            
            return View(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Z-Report");
            TempData["ErrorMessage"] = $"Error generating Z-Report: {ex.Message}";
            return RedirectToAction("History");
        }
    }

    // Z-Report on the receipt printer
    [HttpPost]
    public async Task<IActionResult> PrintZReport(int id)
    {
        var report = await GetClosedSessionZReportAsync(id);
        if (report == null)
            return Json(new { success = false, message = "Z-Report is only available for a closed session" });

        return await PrintToReceiptPrinterAsync(_thermalPrinterService, ReportDocuments.ZReport(report),
            Url.Action(nameof(ZReportPdf), "CashDrawer", new { id }));
    }

    // Z-Report as a PDF file
    [HttpGet]
    public async Task<IActionResult> ZReportPdf(int id)
    {
        var report = await GetClosedSessionZReportAsync(id);
        if (report == null)
            return NotFound();

        return PdfFile(_thermalPrinterService, ReportDocuments.ZReport(report), PdfPaper.A4);
    }

    private async Task<ZReport?> GetClosedSessionZReportAsync(int id)
    {
        var session = await _cashDrawerService.GetSessionWithDetailsAsync(id);
        return session == null || session.IsOpen ? null : await _cashDrawerService.GenerateZReportAsync(id);
    }

    // Get all open sessions (for admins)
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Sales_Edit)]
    public async Task<IActionResult> GetAllOpenSessions()
    {
        try
        {
            var sessions = await _cashDrawerService.GetAllOpenSessionsAsync();
            var data = sessions.Select(s => new {
                id = s.Id,
                userId = s.UserId,
                userName = $"{s.User.FirstName} {s.User.LastName}",
                openedAt = s.OpenedAt,
                openingCash = s.OpeningCash
            });
            
            return Json(new { success = true, data = data });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all open sessions");
            return Json(new { success = false, message = ex.Message });
        }
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 1; // Default to 1 if not found
    }
}

// Request models
public record OpenDrawerRequest(decimal OpeningCash, string? Notes);
public record CloseDrawerRequest(int SessionId, decimal ActualCash, string? Notes);
public record CashDropRequest(int SessionId, decimal Amount, string? Reason, string? Notes);

