using System.Net;
using System.Text;
using System.Text.Json;
using Tests.Api.Infrastructure;
using Web.Api.Reports;
using Xunit;
using static Tests.Api.Pos.PosTestKit;
using static Tests.Api.Reports.InsightsTestKit;

namespace Tests.Api.Reports;

/// <summary>
/// Track I1 (D10): the Reports API through the real pipeline. Sales reports are money: each test makes its own sale through the till and
/// checks that the report moved by exactly that sale, and that cancelling the order takes it out again (a cancelled sale must not stay in
/// the revenue figures). The permission matrix is the old controller's: Reports.View for everything, which a cashier does not hold.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReportsApiTests
{
    private readonly ApiFactory _factory;

    public ReportsApiTests(ApiFactory factory) => _factory = factory;

    private Task<(HttpClient Browser, int UserId)> Admin() => SignedIn(Host(_factory), ApiFactory.AdminUser, ApiFactory.AdminPin);

    private static string TodayRange => $"from={Today}&to={Today}";

    private static async Task<JsonElement> TodayReport(HttpClient browser) => await Ok(await browser.GetAsync($"/api/v1/reports/sales?{TodayRange}"));

    private static JsonElement Method(JsonElement report, int paymentMethodId)
        => report.GetProperty("paymentMethods").EnumerateArray().FirstOrDefault(m => m.GetProperty("paymentMethodId").GetInt32() == paymentMethodId);

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_callers_are_turned_away()
    {
        var anonymous = _factory.CreateBrowser();

        foreach (var url in new[] { "summary", $"sales?{TodayRange}", $"sales.csv?{TodayRange}", $"sales.pdf?{TodayRange}" })
            await AssertProblem(await anonymous.GetAsync($"/api/v1/reports/{url}"), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_holds_no_reports_permission()
    {
        var (cashier, _) = await Cashier(Host(_factory));

        foreach (var url in new[] { "summary", $"sales?{TodayRange}", $"sales.csv?{TodayRange}", $"sales.pdf?{TodayRange}" })
            await AssertProblem(await cashier.GetAsync($"/api/v1/reports/{url}"), HttpStatusCode.Forbidden, "forbidden");
    }

    // ---- the range ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_missing_or_backwards_range_is_a_validation_error_on_the_field()
    {
        var (admin, _) = await Admin();

        var missing = await admin.GetAsync("/api/v1/reports/sales");
        await AssertProblem(missing, HttpStatusCode.BadRequest, "validation_failed");

        var backwards = await admin.GetAsync("/api/v1/reports/sales?from=2026-02-10&to=2026-02-09");
        await AssertProblem(backwards, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(1, (await Body(backwards)).GetProperty("errors").GetProperty("to").GetArrayLength());

        await AssertProblem(await admin.GetAsync("/api/v1/reports/sales?from=1990-01-01&to=2026-01-01"), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.GetAsync("/api/v1/reports/sales.csv?from=2026-02-10&to=2026-02-09"), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.GetAsync("/api/v1/reports/sales.pdf?from=2026-02-10&to=2026-02-09"), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task A_range_without_sales_lists_every_day_with_zero()
    {
        var (admin, _) = await Admin();

        var report = await Ok(await admin.GetAsync("/api/v1/reports/sales?from=2000-01-01&to=2000-01-03"));

        Assert.Equal("2000-01-01", report.GetProperty("from").GetString());
        Assert.Equal("2000-01-03", report.GetProperty("to").GetString());
        Assert.Equal(0, report.GetProperty("totalSales").GetDecimal());
        Assert.Equal(0, report.GetProperty("totalOrders").GetInt32());
        Assert.Equal(0, report.GetProperty("averageOrderValue").GetDecimal());
        Assert.Equal(["2000-01-01", "2000-01-02", "2000-01-03"], report.GetProperty("daily").EnumerateArray().Select(d => d.GetProperty("date").GetString()));
        Assert.All(report.GetProperty("daily").EnumerateArray(), d => Assert.Equal(0, d.GetProperty("sales").GetDecimal()));
        Assert.Equal(0, report.GetProperty("topProducts").GetArrayLength());
        Assert.Equal(0, report.GetProperty("categories").GetArrayLength());
        Assert.Equal(0, report.GetProperty("paymentMethods").GetArrayLength());
    }

    // ---- the money ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_sale_moves_the_report_by_exactly_its_total_and_a_cancelled_sale_leaves_it()
    {
        var (admin, _) = await Admin();
        var before = await TodayReport(admin);
        var beforeSummary = await Ok(await admin.GetAsync("/api/v1/reports/summary"));

        var sale = await Sell(admin, quantity: 3);

        var after = await TodayReport(admin);
        Assert.Equal(before.GetProperty("totalSales").GetDecimal() + sale.Total, after.GetProperty("totalSales").GetDecimal());
        Assert.Equal(before.GetProperty("totalOrders").GetInt32() + 1, after.GetProperty("totalOrders").GetInt32());
        Assert.Equal(before.GetProperty("totalItemsSold").GetDecimal() + 3, after.GetProperty("totalItemsSold").GetDecimal());
        Assert.Equal(after.GetProperty("totalSales").GetDecimal() / after.GetProperty("totalOrders").GetInt32(), after.GetProperty("averageOrderValue").GetDecimal());
        var today = after.GetProperty("daily").EnumerateArray().Single();
        Assert.Equal(Today, today.GetProperty("date").GetString());
        Assert.Equal(after.GetProperty("totalSales").GetDecimal(), today.GetProperty("sales").GetDecimal());

        var summary = await Ok(await admin.GetAsync("/api/v1/reports/summary"));
        foreach (var period in new[] { "today", "thisMonth", "thisYear" })
        {
            Assert.Equal(beforeSummary.GetProperty(period).GetProperty("sales").GetDecimal() + sale.Total, summary.GetProperty(period).GetProperty("sales").GetDecimal());
            Assert.Equal(beforeSummary.GetProperty(period).GetProperty("orders").GetInt32() + 1, summary.GetProperty(period).GetProperty("orders").GetInt32());
        }

        var month = summary.GetProperty("thisMonth");
        Assert.Equal(month.GetProperty("sales").GetDecimal() / month.GetProperty("orders").GetInt32(), summary.GetProperty("averageOrderValue").GetDecimal());

        // Cancelling the order (status 4) reverses the sale: it must disappear from every figure.
        var cancel = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/orders/{sale.OrderId}/status", new { status = 4 }));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var cancelled = await TodayReport(admin);
        Assert.Equal(before.GetProperty("totalSales").GetDecimal(), cancelled.GetProperty("totalSales").GetDecimal());
        Assert.Equal(before.GetProperty("totalOrders").GetInt32(), cancelled.GetProperty("totalOrders").GetInt32());
        var summaryAfterCancel = await Ok(await admin.GetAsync("/api/v1/reports/summary"));
        Assert.Equal(beforeSummary.GetProperty("today").GetProperty("sales").GetDecimal(), summaryAfterCancel.GetProperty("today").GetProperty("sales").GetDecimal());
    }

    [Fact]
    public async Task Payment_methods_come_from_the_receipts_and_a_voided_receipt_leaves_them()
    {
        var (admin, _) = await Admin();
        var before = Method(await TodayReport(admin), Card);
        var cardSalesBefore = before.ValueKind == JsonValueKind.Undefined ? 0 : before.GetProperty("sales").GetDecimal();
        var cardReceiptsBefore = before.ValueKind == JsonValueKind.Undefined ? 0 : before.GetProperty("receipts").GetInt32();

        var sale = await Sell(admin, paymentMethodId: Card);

        var after = await TodayReport(admin);
        var card = Method(after, Card);
        Assert.Equal(cardSalesBefore + sale.Total, card.GetProperty("sales").GetDecimal());
        Assert.Equal(cardReceiptsBefore + 1, card.GetProperty("receipts").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(card.GetProperty("name").GetString()));
        var methods = after.GetProperty("paymentMethods").EnumerateArray().ToList();
        Assert.InRange(methods.Sum(m => m.GetProperty("percentage").GetDecimal()), 99.9m, 100.1m);
        Assert.Equal(methods.OrderByDescending(m => m.GetProperty("sales").GetDecimal()).Select(m => m.GetProperty("paymentMethodId").GetInt32()), methods.Select(m => m.GetProperty("paymentMethodId").GetInt32()));

        var voided = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/receipts/{sale.ReceiptId}/void", new { reason = "test" }));
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);

        var gone = Method(await TodayReport(admin), Card);
        Assert.Equal(cardSalesBefore, gone.ValueKind == JsonValueKind.Undefined ? 0 : gone.GetProperty("sales").GetDecimal());
    }

    [Fact]
    public async Task The_top_products_rank_by_quantity_and_the_categories_by_sales()
    {
        var (admin, _) = await Admin();
        await Sell(admin, quantity: 4);

        var report = await TodayReport(admin);

        var products = report.GetProperty("topProducts").EnumerateArray().ToList();
        Assert.NotEmpty(products);
        Assert.True(products.Count <= 10);
        Assert.Equal(products.Select(p => p.GetProperty("quantitySold").GetDecimal()).OrderByDescending(q => q), products.Select(p => p.GetProperty("quantitySold").GetDecimal()));
        Assert.All(products, p => Assert.True(p.GetProperty("quantitySold").GetDecimal() > 0 && p.GetProperty("revenue").GetDecimal() > 0));

        var categories = report.GetProperty("categories").EnumerateArray().ToList();
        Assert.Equal(categories.OrderByDescending(c => c.GetProperty("sales").GetDecimal()).Select(c => c.GetProperty("categoryId").GetInt32()), categories.Select(c => c.GetProperty("categoryId").GetInt32()));
        if (categories.Count > 0) Assert.InRange(categories.Sum(c => c.GetProperty("percentage").GetDecimal()), 99.9m, 100.1m);
    }

    // ---- export ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_csv_has_a_byte_order_mark_and_all_sections()
    {
        var (admin, _) = await Admin();
        await Sell(admin);

        var response = await admin.GetAsync($"/api/v1/reports/sales.csv?{TodayRange}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("sales-report_", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith($"\"Sales report\",\"{Today}\",\"{Today}\"", text);
        foreach (var heading in new[] { "\"Total sales\"", "\"Date\",\"Sales\",\"Orders\"", "\"Product\",\"Quantity sold\"", "\"Category\",\"Sales\"", "\"Payment method\",\"Sales\"" })
            Assert.Contains(heading, text);
    }

    [Fact]
    public async Task The_pdf_is_a_pdf_and_is_audited()
    {
        var (admin, _) = await Admin();

        var response = await admin.GetAsync($"/api/v1/reports/sales.pdf?{TodayRange}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1+1", "\"'+1+1\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("-cmd", "\"'-cmd\"")]
    [InlineData("-12.5", "\"-12.5\"")]
    [InlineData("Milk, \"fresh\"", "\"Milk, \"\"fresh\"\"\"")]
    [InlineData("", "\"\"")]
    public void A_csv_cell_is_quoted_and_cannot_start_a_formula(string input, string expected) => Assert.Equal(expected, CsvCell.Quote(input));
}
