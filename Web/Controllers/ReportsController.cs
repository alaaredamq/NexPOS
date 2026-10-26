using Application.DTOs.Reports;
using Application.IServices.Reports;
using Core.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers
{
    [Authorize]
    [RequirePermission(PermissionHelper.Permissions.Reports_View)]
    public class ReportsController : BaseController
    {
        private readonly ISalesReportService _salesReportService;

        public ReportsController(ISalesReportService salesReportService)
        {
            _salesReportService = salesReportService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult SalesReport()
        {
            // Use UTC dates to match OrderDate which is stored in UTC
            var utcNow = DateTime.UtcNow;
            var model = new SalesReportRequest
            {
                StartDate = utcNow.Date.AddDays(-30),
                EndDate = utcNow.Date.AddDays(1).AddTicks(-1), // End of today in UTC
                ReportType = "daily"
            };
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSalesReport(SalesReportRequest request)
        {
            try
            {
                var report = await _salesReportService.GetSalesReportAsync(request);
                return Json(new { success = true, data = report });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDailySalesReport(DateTime date)
        {
            try
            {
                var report = await _salesReportService.GetDailySalesReportAsync(date);
                return Json(new { success = true, data = report });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMonthlySalesReport(int year, int month)
        {
            try
            {
                var report = await _salesReportService.GetMonthlySalesReportAsync(year, month);
                return Json(new { success = true, data = report });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetYearlySalesReport(int year)
        {
            try
            {
                var report = await _salesReportService.GetYearlySalesReportAsync(year);
                return Json(new { success = true, data = report });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSalesTrend(DateTime startDate, DateTime endDate)
        {
            try
            {
                // Picked dates are local days; orders are stored in UTC.
                var (fromUtc, toUtc) = AppTime.LocalDayRangeToUtc(startDate, endDate);
                var trend = await _salesReportService.GetSalesTrendAsync(fromUtc, toUtc);
                return Json(new { success = true, data = trend });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTopProducts(DateTime startDate, DateTime endDate, int limit = 10)
        {
            try
            {
                var (fromUtc, toUtc) = AppTime.LocalDayRangeToUtc(startDate, endDate);
                var products = await _salesReportService.GetTopProductsAsync(fromUtc, toUtc, limit);
                return Json(new { success = true, data = products });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetCategorySales(DateTime startDate, DateTime endDate)
        {
            try
            {
                var (fromUtc, toUtc) = AppTime.LocalDayRangeToUtc(startDate, endDate);
                var categorySales = await _salesReportService.GetCategorySalesAsync(fromUtc, toUtc);
                return Json(new { success = true, data = categorySales });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardStats()
        {
            try
            {
                // Today / this month / this year are LOCAL calendar periods; convert their edges to UTC for the query.
                var nowLocal = DateTime.Now;
                var todayLocal = nowLocal.Date;
                var monthLocal = new DateTime(nowLocal.Year, nowLocal.Month, 1);
                var yearLocal = new DateTime(nowLocal.Year, 1, 1);

                var today = todayLocal.LocalToUtc();
                var tomorrow = todayLocal.AddDays(1).LocalToUtc();
                var thisMonth = monthLocal.LocalToUtc();
                var nextMonth = monthLocal.AddMonths(1).LocalToUtc();
                var thisYear = yearLocal.LocalToUtc();
                var nextYear = yearLocal.AddYears(1).LocalToUtc();

                var todaySales = await _salesReportService.GetTotalSalesAsync(today, tomorrow);
                var thisMonthSales = await _salesReportService.GetTotalSalesAsync(thisMonth, nextMonth);
                var thisYearSales = await _salesReportService.GetTotalSalesAsync(thisYear, nextYear);

                var todayOrders = await _salesReportService.GetTotalOrdersAsync(today, tomorrow);
                var thisMonthOrders = await _salesReportService.GetTotalOrdersAsync(thisMonth, nextMonth);
                var thisYearOrders = await _salesReportService.GetTotalOrdersAsync(thisYear, nextYear);

                var stats = new
                {
                    Today = new { Sales = todaySales, Orders = todayOrders },
                    ThisMonth = new { Sales = thisMonthSales, Orders = thisMonthOrders },
                    ThisYear = new { Sales = thisYearSales, Orders = thisYearOrders }
                };

                return Json(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportSalesReport(SalesReportRequest request, string format = "pdf")
        {
            try
            {
                var report = await _salesReportService.GetSalesReportAsync(request);
                
                // For now, return JSON data. In a real implementation, you would generate PDF/Excel files
                var fileName = $"SalesReport_{request.StartDate:yyyyMMdd}_{request.EndDate:yyyyMMdd}.{format}";
                
                return Json(new { 
                    success = true, 
                    message = "Export functionality will be implemented with a PDF/Excel library",
                    fileName = fileName,
                    data = report 
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}