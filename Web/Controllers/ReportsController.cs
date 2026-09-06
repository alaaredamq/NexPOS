using Application.DTOs.Reports;
using Application.IServices.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;

namespace Web.Controllers
{
    [Authorize]
    [RequirePermission("Reports.View")]
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
                var trend = await _salesReportService.GetSalesTrendAsync(startDate, endDate);
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
                var products = await _salesReportService.GetTopProductsAsync(startDate, endDate, limit);
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
                var categorySales = await _salesReportService.GetCategorySalesAsync(startDate, endDate);
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
                var utcNow = DateTime.UtcNow;
                var today = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc);
                var thisMonth = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var thisYear = new DateTime(utcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                var todaySales = await _salesReportService.GetTotalSalesAsync(today, today.AddDays(1));
                var thisMonthSales = await _salesReportService.GetTotalSalesAsync(thisMonth, thisMonth.AddMonths(1));
                var thisYearSales = await _salesReportService.GetTotalSalesAsync(thisYear, thisYear.AddYears(1));

                var todayOrders = await _salesReportService.GetTotalOrdersAsync(today, today.AddDays(1));
                var thisMonthOrders = await _salesReportService.GetTotalOrdersAsync(thisMonth, thisMonth.AddMonths(1));
                var thisYearOrders = await _salesReportService.GetTotalOrdersAsync(thisYear, thisYear.AddYears(1));

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