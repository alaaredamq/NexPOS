using System.Globalization;
using System.Text;
using Application.DTOs.Reports;
using Application.IServices.Base;
using Application.IServices.Reports;
using Application.IServices.Sales;
using Application.Services.Printing;
using Core.Auditing;
using Core.Printing;
using Core.Time;
using Web.Api.Reports.Contracts;
using static Web.Helpers.CurrencyHelper;

namespace Web.Api.Reports;

/// <summary>Quotes one CSV cell.</summary>
public static class CsvCell
{
    /// <summary>Quoted; a leading = + - @ is defused so a product named "=cmd()" is text in Excel, not a formula (a plain negative number stays a number).</summary>
    public static string Quote(string value)
    {
        if (value.Length > 0 && (value[0] is '=' or '+' or '@' || value[0] == '-' && (value.Length == 1 || !char.IsDigit(value[1])))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

public interface IReportsApiService
{
    Task<ReportsSummaryDto> SummaryAsync();

    Task<SalesReportDto> SalesAsync(DateOnly from, DateOnly to);

    /// <summary>The report as a UTF-8 CSV (with a byte-order mark so Excel reads Arabic names).</summary>
    Task<byte[]> CsvAsync(DateOnly from, DateOnly to);

    Task<(byte[] Pdf, string FileName)> PdfAsync(DateOnly from, DateOnly to);
}

/// <summary>
/// The sales figures of the old Reports pages, through the same <see cref="ISalesReportService"/> (same order filter: not deleted, not cancelled,
/// not returned; same local-day to UTC conversion). Two things are new: the payment-method split comes from the receipts (the old service
/// answered "100% Cash" for everything), and days without sales are listed with zero so a chart has no gaps.
/// </summary>
public sealed class ReportsApiService : IReportsApiService
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly ISalesReportService _report;
    private readonly IReceiptService _receipts;
    private readonly ICurrencyService _currencies;
    private readonly IThermalPrinterService _printer;
    private readonly IAuditTrail _audit;

    public ReportsApiService(ISalesReportService report, IReceiptService receipts, ICurrencyService currencies, IThermalPrinterService printer, IAuditTrail audit)
    {
        _report = report;
        _receipts = receipts;
        _currencies = currencies;
        _printer = printer;
        _audit = audit;
    }

    public async Task<ReportsSummaryDto> SummaryAsync()
    {
        // Today / this month / this year are LOCAL calendar periods; their edges are converted to UTC for the query (as the old controller did).
        var today = DateTime.Now.Date;
        var month = new DateTime(today.Year, today.Month, 1);
        var year = new DateTime(today.Year, 1, 1);

        var todayPeriod = await PeriodAsync(today, today.AddDays(1));
        var monthPeriod = await PeriodAsync(month, month.AddMonths(1));
        var yearPeriod = await PeriodAsync(year, year.AddYears(1));

        return new ReportsSummaryDto
        {
            CurrencyNotation = await NotationAsync(),
            Today = todayPeriod,
            ThisMonth = monthPeriod,
            ThisYear = yearPeriod,
            AverageOrderValue = monthPeriod.Orders > 0 ? monthPeriod.Sales / monthPeriod.Orders : 0,
        };
    }

    private async Task<SalesPeriodDto> PeriodAsync(DateTime startLocal, DateTime endLocalExclusive)
    {
        var start = startLocal.LocalToUtc();
        var end = endLocalExclusive.LocalToUtc().AddTicks(-1);
        return new SalesPeriodDto
        {
            Sales = await _report.GetTotalSalesAsync(start, end),
            Profit = await _report.GetTotalProfitAsync(start, end),
            Orders = await _report.GetTotalOrdersAsync(start, end),
        };
    }

    public async Task<SalesReportDto> SalesAsync(DateOnly from, DateOnly to)
    {
        var request = new SalesReportRequest
        {
            StartDate = from.ToDateTime(TimeOnly.MinValue),
            EndDate = to.ToDateTime(TimeOnly.MinValue),
            ReportType = "daily",
            IncludeProductDetails = true,
            IncludeCategoryBreakdown = true,
            IncludePaymentMethodBreakdown = false, // computed below from the receipts
        };
        var report = await _report.GetSalesReportAsync(request);

        var salesByDay = report.DailySales.ToDictionary(d => DateOnly.FromDateTime(d.Date), d => d);
        var daily = new List<SalesReportDayDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            salesByDay.TryGetValue(day, out var found);
            daily.Add(new SalesReportDayDto { Date = Iso(day), Sales = found?.Sales ?? 0, Orders = found?.Orders ?? 0 });
        }

        return new SalesReportDto
        {
            From = Iso(from),
            To = Iso(to),
            CurrencyNotation = await NotationAsync(),
            TotalSales = report.TotalSales,
            TotalProfit = report.TotalProfit,
            TotalOrders = report.TotalOrders,
            AverageOrderValue = report.AverageOrderValue,
            TotalItemsSold = report.TotalItemsSold,
            Daily = daily,
            TopProducts = report.TopProducts
                .Select(p => new SalesReportProductDto { ProductId = p.ProductId, Name = p.ProductName, QuantitySold = p.QuantitySold, Revenue = p.TotalRevenue, AveragePrice = p.AveragePrice })
                .ToList(),
            Categories = report.CategorySales
                .Select(c => new SalesReportCategoryDto { CategoryId = c.CategoryId, Name = c.CategoryName, Sales = c.TotalSales, Percentage = c.Percentage })
                .ToList(),
            PaymentMethods = await PaymentMethodsAsync(from, to),
        };
    }

    /// <summary>Receipt totals per payment method over the range (void receipts - a cancelled sale's - are left out).</summary>
    private async Task<IReadOnlyList<SalesReportPaymentDto>> PaymentMethodsAsync(DateOnly from, DateOnly to)
    {
        var (fromUtc, toUtc) = AppTime.LocalDayRangeToUtc(from.ToDateTime(TimeOnly.MinValue), to.ToDateTime(TimeOnly.MinValue));
        var receipts = (await _receipts.GetReceiptsByDateRangeAsync(fromUtc, toUtc)).Where(r => !r.IsVoid).ToList();

        var groups = receipts
            .GroupBy(r => (r.PaymentMethodId, Name: r.PaymentMethod?.Name ?? string.Empty))
            .Select(g => (g.Key.PaymentMethodId, g.Key.Name, Sales: g.Sum(r => r.TotalAmount), Count: g.Count()))
            .OrderByDescending(g => g.Sales)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var total = groups.Sum(g => g.Sales);

        return groups
            .Select(g => new SalesReportPaymentDto
            {
                PaymentMethodId = g.PaymentMethodId,
                Name = g.Name,
                Sales = g.Sales,
                Receipts = g.Count,
                Percentage = total > 0 ? g.Sales / total * 100 : 0,
            })
            .ToList();
    }

    // ---- export -------------------------------------------------------------------------------------------------

    public async Task<byte[]> CsvAsync(DateOnly from, DateOnly to)
    {
        var report = await SalesAsync(from, to);
        var csv = new StringBuilder();

        void Row(params string[] cells) => csv.Append(string.Join(',', cells.Select(CsvCell.Quote))).Append("\r\n");

        Row("Sales report", report.From, report.To);
        Row("Currency", report.CurrencyNotation);
        Row("Total sales", Number(report.TotalSales));
        Row("Total profit", Number(report.TotalProfit));
        Row("Orders", report.TotalOrders.ToString(Inv));
        Row("Average order value", Number(report.AverageOrderValue));
        Row("Items sold", Number(report.TotalItemsSold));

        csv.Append("\r\n");
        Row("Date", "Sales", "Orders");
        foreach (var d in report.Daily) Row(d.Date, Number(d.Sales), d.Orders.ToString(Inv));

        csv.Append("\r\n");
        Row("Product", "Quantity sold", "Revenue", "Average price");
        foreach (var p in report.TopProducts) Row(p.Name, Number(p.QuantitySold), Number(p.Revenue), Number(p.AveragePrice));

        csv.Append("\r\n");
        Row("Category", "Sales", "Share %");
        foreach (var c in report.Categories) Row(c.Name, Number(c.Sales), Number(c.Percentage));

        csv.Append("\r\n");
        Row("Payment method", "Sales", "Receipts", "Share %");
        foreach (var m in report.PaymentMethods) Row(m.Name, Number(m.Sales), m.Receipts.ToString(Inv), Number(m.Percentage));

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    public async Task<(byte[] Pdf, string FileName)> PdfAsync(DateOnly from, DateOnly to)
    {
        var report = await SalesAsync(from, to);
        string Money(decimal amount) => FormatCurrency(amount, report.CurrencyNotation);

        var document = ReportDocuments.SalesReport(
            report.From,
            report.To,
            new (string, string)[]
            {
                ("Total sales", Money(report.TotalSales)),
                ("Total profit", Money(report.TotalProfit)),
                ("Orders", report.TotalOrders.ToString(Inv)),
                ("Average order", Money(report.AverageOrderValue)),
                ("Items sold", report.TotalItemsSold.ToString("0.###", Inv)),
            },
            new[]
            {
                new ReportSection("TOP PRODUCTS", report.TopProducts.Select(p => (p.Name, $"{p.QuantitySold.ToString("0.###", Inv)} x = {Money(p.Revenue)}")).ToList()),
                new ReportSection("CATEGORIES", report.Categories.Select(c => (c.Name, $"{Money(c.Sales)} ({c.Percentage.ToString("0.#", Inv)}%)")).ToList()),
                new ReportSection("PAYMENT METHODS", report.PaymentMethods.Select(m => (m.Name, $"{Money(m.Sales)} ({m.Percentage.ToString("0.#", Inv)}%)")).ToList()),
                new ReportSection("DAILY", report.Daily.Where(d => d.Orders > 0).Select(d => (d.Date, $"{d.Orders.ToString(Inv)} / {Money(d.Sales)}")).ToList()),
            });

        var pdf = _printer.GeneratePdf(document, PdfPaper.A4);
        _audit.LogBusiness(AuditActions.PdfGenerated, $"PDF generated: '{document.Title}'", "PrintJob",
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, paper = nameof(PdfPaper.A4), bytes = pdf.Length });
        return (pdf, $"{document.FileName}.pdf");
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private async Task<string> NotationAsync() => (await _currencies.GetDefaultAsync())?.Notation ?? "$";

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", Inv);

    private static string Number(decimal value) => value.ToString("0.##", Inv);
}
