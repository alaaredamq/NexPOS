using System.Net;
using System.Text.Json;
using Application.IServices.Logging;
using Core.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.Inventory;

/// <summary>
/// Inventory API (track C3): the permission matrix of the old controller, the stock-adjustment rules (money-adjacent: stock
/// feeds the stock value and the POS), minimum stock, the list / tile figures, and both reports with their PDF.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InventoryApiTests : CatalogApiTestBase
{
    public InventoryApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..22];

    /// <summary>A product with the given stock / minimum, built from the shop's own first category and supplier so the foreign keys exist.</summary>
    private static async Task<(int Id, string Name)> CreateProductAsync(HttpClient admin, decimal stock, decimal? minimum = null, decimal purchase = 4m, decimal selling = 6m, bool active = true)
    {
        var lookups = await Body(await admin.GetAsync("/api/v1/products/lookups"));
        var name = Unique("Inv");
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["description"] = "made by an inventory test",
            ["barcode"] = "I" + Guid.NewGuid().ToString("N")[..12],
            ["categoryId"] = lookups.GetProperty("categories")[0].GetProperty("id").GetInt32(),
            ["supplierId"] = lookups.GetProperty("suppliers")[0].GetProperty("id").GetInt32(),
            ["currencyId"] = 0,
            ["purchasePrice"] = purchase,
            ["sellingPrice"] = selling,
            ["stockQuantity"] = stock,
            ["minStockLevel"] = minimum ?? 0m,
            ["isActive"] = active,
            ["unit"] = "piece",
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await Body(response)).GetProperty("id").GetInt32(), name);
    }

    private static async Task<JsonElement> FindRowAsync(HttpClient client, string name)
    {
        var page = await Body(await client.GetAsync($"/api/v1/inventory?q={name}&pageSize=200"));
        return page.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("name").GetString() == name);
    }

    private static async Task<decimal> StockOfAsync(HttpClient client, int productId)
        => (await Body(await client.GetAsync($"/api/v1/products/{productId}"))).GetProperty("stockQuantity").GetDecimal();

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_the_list_answers_401()
    {
        var response = await Factory.CreateBrowser().GetAsync("/api/v1/inventory");

        await AssertProblem(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_holds_no_inventory_permission()
    {
        var cashier = await Cashier();

        foreach (var url in new[]
                 {
                     "/api/v1/inventory", "/api/v1/inventory/summary", "/api/v1/inventory/reports/low-stock", "/api/v1/inventory/reports/stock-value",
                     "/api/v1/inventory/reports/low-stock.pdf", "/api/v1/inventory/reports/stock-value.pdf",
                 })
            await AssertProblem(await cashier.GetAsync(url), HttpStatusCode.Forbidden, "forbidden");

        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/inventory/1/adjust", new { type = "add", quantity = 1 })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Put, "/api/v1/inventory/1/minimum-stock", new { minimumStock = 1 })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/inventory/reports/low-stock/print")), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task An_adjustment_without_the_client_header_is_refused()
    {
        var admin = await Admin();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/1/adjust")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { type = "add", quantity = 1 }),
        };

        await AssertProblem(await admin.SendAsync(request), HttpStatusCode.Forbidden, "client_header_required");
    }

    // ---- list, tiles -------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_shows_status_value_and_pages_sorts_and_searches()
    {
        var admin = await Admin();
        var (outId, outName) = await CreateProductAsync(admin, stock: 0, minimum: 3);
        var (_, lowName) = await CreateProductAsync(admin, stock: 2, minimum: 5, purchase: 4m);
        var (_, okName) = await CreateProductAsync(admin, stock: 10, minimum: 5, purchase: 3m);

        var outRow = await FindRowAsync(admin, outName);
        var lowRow = await FindRowAsync(admin, lowName);
        var okRow = await FindRowAsync(admin, okName);

        Assert.Equal("out", outRow.GetProperty("status").GetString());
        Assert.Equal("low", lowRow.GetProperty("status").GetString());
        Assert.Equal("ok", okRow.GetProperty("status").GetString());
        Assert.Equal(outId, outRow.GetProperty("id").GetInt32());
        Assert.Equal(8m, lowRow.GetProperty("stockValue").GetDecimal());   // 2 x cost 4
        Assert.Equal(30m, okRow.GetProperty("stockValue").GetDecimal());   // 10 x cost 3
        Assert.Equal(5m, lowRow.GetProperty("minimumStock").GetDecimal());
        Assert.False(string.IsNullOrEmpty(lowRow.GetProperty("currencyNotation").GetString()));

        // Search is a substring of name; sort by stock descending puts the ok row first.
        var sorted = await Body(await admin.GetAsync($"/api/v1/inventory?q=Inv-&sort=stock&dir=desc&pageSize=200"));
        var stocks = sorted.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("currentStock").GetDecimal()).ToList();
        Assert.Equal(stocks.OrderByDescending(s => s), stocks);
        Assert.Equal(1, (await Body(await admin.GetAsync($"/api/v1/inventory?q={okName}"))).GetProperty("total").GetInt32());

        var paged = await Body(await admin.GetAsync("/api/v1/inventory?pageSize=2&page=2"));
        Assert.Equal(2, paged.GetProperty("items").GetArrayLength());
        Assert.Equal(2, paged.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task The_summary_counts_match_the_whole_list()
    {
        var admin = await Admin();
        await CreateProductAsync(admin, stock: 0, minimum: 3);
        await CreateProductAsync(admin, stock: 1, minimum: 4);

        var all = await Body(await admin.GetAsync("/api/v1/inventory?pageSize=200"));
        var rows = all.GetProperty("items").EnumerateArray().ToList();
        var summary = await Body(await admin.GetAsync("/api/v1/inventory/summary"));

        // The demo database is small enough for one page of 200; if it ever is not, this fails loudly instead of lying.
        Assert.Equal(all.GetProperty("total").GetInt32(), rows.Count);
        Assert.Equal(rows.Count, summary.GetProperty("totalProducts").GetInt32());
        Assert.Equal(rows.Count(r => r.GetProperty("currentStock").GetDecimal() <= 0), summary.GetProperty("outOfStockCount").GetInt32());
        Assert.Equal(
            rows.Count(r => r.GetProperty("minimumStock").ValueKind != JsonValueKind.Null && r.GetProperty("currentStock").GetDecimal() <= r.GetProperty("minimumStock").GetDecimal()),
            summary.GetProperty("lowStockCount").GetInt32());
        Assert.False(string.IsNullOrEmpty(summary.GetProperty("currencyNotation").GetString()));
    }

    // ---- adjusting stock ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Adding_and_removing_stock_changes_the_product_and_returns_both_figures()
    {
        var admin = await Admin();
        var (id, _) = await CreateProductAsync(admin, stock: 10);

        var added = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/{id}/adjust", new { type = "add", quantity = 5.5m, reason = "delivery" }));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var addedBody = await Body(added);
        Assert.Equal(10m, addedBody.GetProperty("previousStock").GetDecimal());
        Assert.Equal(15.5m, addedBody.GetProperty("newStock").GetDecimal());
        Assert.Equal(15.5m, addedBody.GetProperty("item").GetProperty("currentStock").GetDecimal());
        Assert.Equal(15.5m, await StockOfAsync(admin, id));

        var removed = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/{id}/adjust", new { type = "Remove", quantity = 15.5m }));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(0m, (await Body(removed)).GetProperty("newStock").GetDecimal());
        Assert.Equal(0m, await StockOfAsync(admin, id));
    }

    [Fact]
    public async Task Removing_more_than_is_in_stock_is_a_field_error_and_changes_nothing()
    {
        var admin = await Admin();
        var (id, _) = await CreateProductAsync(admin, stock: 3);

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/{id}/adjust", new { type = "remove", quantity = 3.5m }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await Body(response);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("quantity", out _));
        Assert.Equal(3m, await StockOfAsync(admin, id));
    }

    [Theory]
    [InlineData("add", 0)]
    [InlineData("add", -2)]
    [InlineData("remove", -1)]
    [InlineData("swap", 1)]
    public async Task A_zero_or_negative_quantity_or_an_unknown_type_is_refused(string type, decimal quantity)
    {
        var admin = await Admin();
        var (id, _) = await CreateProductAsync(admin, stock: 4);

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/{id}/adjust", new { type, quantity }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(4m, await StockOfAsync(admin, id)); // the old page let a negative "add" take stock away; the API does not
    }

    [Fact]
    public async Task Adjusting_an_unknown_product_is_a_404()
    {
        var admin = await Admin();

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/inventory/999999/adjust", new { type = "add", quantity = 1 }));

        await AssertProblem(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task An_adjustment_writes_a_business_audit_row_with_the_reason()
    {
        var admin = await Admin();
        var (id, name) = await CreateProductAsync(admin, stock: 8);
        var reason = Unique("breakage");

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/{id}/adjust", new { type = "remove", quantity = 2, reason }))).StatusCode);

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditTrail>().FlushAsync();
        var (items, _) = await scope.ServiceProvider.GetRequiredService<IAuditLogService>()
            .SearchAsync(new AuditLogFilter { Action = AuditActions.StockAdjusted, EntityId = id }, 0, 10);

        var row = Assert.Single(items);
        Assert.Equal("Product", row.EntityType);
        Assert.Equal(name, row.EntityLabel);
        Assert.Contains(reason, row.AdditionalInfoJson);
        Assert.Contains("layla", row.Username, StringComparison.OrdinalIgnoreCase);
    }

    // ---- minimum stock -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Setting_the_minimum_moves_the_product_between_ok_and_low()
    {
        var admin = await Admin();
        var (id, name) = await CreateProductAsync(admin, stock: 6, minimum: 2);
        Assert.Equal("ok", (await FindRowAsync(admin, name)).GetProperty("status").GetString());

        var raised = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/inventory/{id}/minimum-stock", new { minimumStock = 10 }));
        Assert.Equal(HttpStatusCode.OK, raised.StatusCode);
        Assert.Equal("low", (await Body(raised)).GetProperty("status").GetString());
        Assert.Equal(6m, await StockOfAsync(admin, id)); // only the minimum changed

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/inventory/{id}/minimum-stock", new { minimumStock = -1 })), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Put, "/api/v1/inventory/999999/minimum-stock", new { minimumStock = 1 })), HttpStatusCode.NotFound, "not_found");
    }

    // ---- reports -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_low_stock_report_lists_least_stock_first_with_deficit_and_priority()
    {
        var admin = await Admin();
        var (_, critical) = await CreateProductAsync(admin, stock: 0, minimum: 10);
        var (_, high) = await CreateProductAsync(admin, stock: 4, minimum: 10);
        var (_, medium) = await CreateProductAsync(admin, stock: 8, minimum: 10);
        var (_, fine) = await CreateProductAsync(admin, stock: 50, minimum: 10);

        var report = await Body(await admin.GetAsync("/api/v1/inventory/reports/low-stock?pageSize=200"));
        var items = report.GetProperty("items").GetProperty("items").EnumerateArray().ToList();
        var byName = items.GroupBy(i => i.GetProperty("name").GetString()!).ToDictionary(g => g.Key, g => g.First()); // other tests leave same-named products behind

        Assert.False(byName.ContainsKey(fine));
        Assert.Equal("critical", byName[critical].GetProperty("priority").GetString());
        Assert.Equal(10m, byName[critical].GetProperty("deficit").GetDecimal());
        Assert.Equal("high", byName[high].GetProperty("priority").GetString());   // 4 <= half of 10
        Assert.Equal(6m, byName[high].GetProperty("deficit").GetDecimal());
        Assert.Equal("medium", byName[medium].GetProperty("priority").GetString());
        Assert.Equal(2m, byName[medium].GetProperty("deficit").GetDecimal());

        var stocks = items.Select(i => i.GetProperty("currentStock").GetDecimal()).ToList();
        Assert.Equal(stocks.OrderBy(s => s), stocks);

        var summary = report.GetProperty("summary");
        Assert.Equal(items.Count, summary.GetProperty("products").GetInt32());
        Assert.Equal(items.Count(i => i.GetProperty("currentStock").GetDecimal() <= 0), summary.GetProperty("outOfStock").GetInt32());
        Assert.Equal(items.Sum(i => i.GetProperty("currentStock").GetDecimal()), summary.GetProperty("totalUnits").GetDecimal());

        var searched = await Body(await admin.GetAsync($"/api/v1/inventory/reports/low-stock?q={high}"));
        Assert.Equal(1, searched.GetProperty("items").GetProperty("total").GetInt32());
        Assert.Equal(items.Count, searched.GetProperty("summary").GetProperty("products").GetInt32()); // totals ignore the search
    }

    [Fact]
    public async Task The_stock_value_report_adds_up_and_leaves_out_inactive_products()
    {
        var admin = await Admin();
        var (_, active) = await CreateProductAsync(admin, stock: 100, purchase: 50m, selling: 60m);
        var (_, inactive) = await CreateProductAsync(admin, stock: 100, purchase: 50m, selling: 60m, active: false);

        var report = await Body(await admin.GetAsync("/api/v1/inventory/reports/stock-value?pageSize=200"));
        var rows = report.GetProperty("items").GetProperty("items").EnumerateArray().ToList();
        var names = rows.Select(r => r.GetProperty("name").GetString()).ToList();

        Assert.Contains(active, names);
        Assert.DoesNotContain(inactive, names);
        Assert.Equal(report.GetProperty("totalProducts").GetInt32(), rows.Count);
        Assert.Equal(rows.Sum(r => r.GetProperty("stockValue").GetDecimal()), report.GetProperty("totalValue").GetDecimal());
        Assert.Equal(report.GetProperty("totalValue").GetDecimal() / rows.Count, report.GetProperty("averageValue").GetDecimal());

        var mine = rows.Single(r => r.GetProperty("name").GetString() == active);
        Assert.Equal(5000m, mine.GetProperty("stockValue").GetDecimal());
        Assert.Equal(50m, mine.GetProperty("unitCost").GetDecimal());

        // Most valuable first, charts capped at 8 / 10, and the biggest product leads the top-10 series.
        var values = rows.Select(r => r.GetProperty("stockValue").GetDecimal()).ToList();
        Assert.Equal(values.OrderByDescending(v => v), values);
        Assert.True(report.GetProperty("byCategory").GetArrayLength() is >= 1 and <= 8);
        Assert.True(report.GetProperty("topProducts").GetArrayLength() <= 10);
        Assert.Equal(values[0], report.GetProperty("topProducts")[0].GetProperty("value").GetDecimal());

        var percent = rows.Sum(r => r.GetProperty("valuePercent").GetDecimal());
        Assert.InRange(percent, 99m, 101m); // each share is rounded to one decimal
    }

    [Fact]
    public async Task The_stock_value_table_sorts_by_the_requested_column()
    {
        var admin = await Admin();
        await CreateProductAsync(admin, stock: 3);
        await CreateProductAsync(admin, stock: 7);

        var byStock = await Body(await admin.GetAsync("/api/v1/inventory/reports/stock-value?sort=stock&dir=asc&pageSize=200"));
        var stocks = byStock.GetProperty("items").GetProperty("items").EnumerateArray().Select(r => r.GetProperty("stockQuantity").GetDecimal()).ToList();

        Assert.Equal(stocks.OrderBy(s => s), stocks);
    }

    [Fact]
    public async Task Both_reports_download_as_a_pdf()
    {
        var admin = await Admin();

        foreach (var url in new[] { "/api/v1/inventory/reports/low-stock.pdf", "/api/v1/inventory/reports/stock-value.pdf" })
        {
            var response = await admin.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 100);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
    }

    [Fact]
    public async Task Printing_answers_200_with_the_outcome_a_message_and_a_pdf_link()
    {
        var admin = await Admin();

        foreach (var (report, pdf) in new[] { ("low-stock", "low-stock.pdf"), ("stock-value", "stock-value.pdf") })
        {
            var response = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/reports/{report}/print"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await Body(response);
            Assert.True(body.GetProperty("printed").GetBoolean()); // the test host runs with NEXPOS_DEMO=1: a no-op printer that reports success, never hardware
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
            Assert.EndsWith("/" + pdf, body.GetProperty("pdfUrl").GetString());
        }
    }
}
