using System.Net;
using System.Text.Json;
using Application.IServices.Logging;
using Core.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.Purchases;

/// <summary>
/// Purchase notes: saving one is "goods received" and moves stock, cost prices and the supplier's balance in one step,
/// cancelling undoes it. These are the paths that cost real money when wrong, so each is checked end to end through the API.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PurchasesApiTests : CatalogApiTestBase
{
    public PurchasesApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..22];

    private static async Task<int> CreateSupplierAsync(HttpClient admin, bool active = true, double discount = 0)
    {
        var currencyId = (await Body(await admin.GetAsync("/api/v1/suppliers/options"))).GetProperty("defaultCurrencyId").GetInt32();
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/suppliers", new Dictionary<string, object?>
        {
            ["name"] = Unique("PurSup"),
            ["currencyId"] = currencyId,
            ["discountRate"] = discount,
            ["isActive"] = active,
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateProductAsync(HttpClient admin, int supplierId, decimal stock = 5m, decimal cost = 4m)
    {
        var lookups = await Body(await admin.GetAsync("/api/v1/products/lookups"));
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", new Dictionary<string, object?>
        {
            ["name"] = Unique("PurProd"),
            ["description"] = "made by a purchase test",
            ["barcode"] = "P" + Guid.NewGuid().ToString("N")[..12],
            ["categoryId"] = lookups.GetProperty("categories")[0].GetProperty("id").GetInt32(),
            ["supplierId"] = supplierId,
            ["currencyId"] = 0,
            ["purchasePrice"] = cost,
            ["sellingPrice"] = 9m,
            ["stockQuantity"] = stock,
            ["minStockLevel"] = 0m,
            ["isActive"] = true,
            ["unit"] = "piece",
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("id").GetInt32();
    }

    private static async Task<decimal> StockAsync(HttpClient client, int productId)
        => (await Body(await client.GetAsync($"/api/v1/products/{productId}"))).GetProperty("stockQuantity").GetDecimal();

    private static async Task<decimal> CostAsync(HttpClient client, int productId)
        => (await Body(await client.GetAsync($"/api/v1/products/{productId}"))).GetProperty("purchasePrice").GetDecimal();

    private static async Task<decimal> BalanceAsync(HttpClient client, int supplierId)
        => (await Body(await client.GetAsync($"/api/v1/suppliers/{supplierId}"))).GetProperty("balance").GetDecimal();

    private static async Task<List<(string Kind, decimal Amount)>> LedgerAsync(HttpClient client, int supplierId)
        => (await Body(await client.GetAsync($"/api/v1/suppliers/{supplierId}/debt-transactions?pageSize=100")))
            .GetProperty("items").EnumerateArray()
            .Select(e => (e.GetProperty("kind").GetString()!, e.GetProperty("amount").GetDecimal()))
            .Reverse().ToList();

    private static object Line(int productId, decimal quantity, decimal unitCost, bool updateCost = true)
        => new { productId, quantity, unitCost, updateCostPrice = updateCost };

    private static Task<HttpResponseMessage> Buy(HttpClient client, int supplierId, object[] lines, decimal discountPercent = 0, decimal paid = 0, string? purchaseDate = null, string? reference = null, bool payFromDrawer = false)
        => client.SendAsync(Json(HttpMethod.Post, "/api/v1/purchases", new
        {
            supplierId,
            purchaseDate,
            supplierReference = reference,
            notes = "test purchase",
            discountPercent,
            amountPaid = paid,
            payFromDrawer,
            lines,
        }));

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_the_list_answers_401()
    {
        await AssertProblem(await Factory.CreateBrowser().GetAsync("/api/v1/purchases"), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_holds_no_purchase_permission()
    {
        var cashier = await Cashier();

        foreach (var url in new[] { "/api/v1/purchases", "/api/v1/purchases/summary", "/api/v1/purchases/export", "/api/v1/purchases/1", "/api/v1/purchases/1/pdf" })
            await AssertProblem(await cashier.GetAsync(url), HttpStatusCode.Forbidden, "forbidden");

        await AssertProblem(await Buy(cashier, 1, [Line(1, 1, 1)]), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/purchases/1/cancel", new { reason = "x" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/purchases/1/print")), HttpStatusCode.Forbidden, "forbidden");
    }

    // ---- saving ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Saving_a_purchase_adds_stock_sets_the_cost_and_puts_the_unpaid_part_on_the_supplier()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var oil = await CreateProductAsync(admin, supplier, stock: 5m, cost: 4m);
        var rice = await CreateProductAsync(admin, supplier, stock: 0m, cost: 1m);

        // 10 x 8 = 80 and 2.5 x 4 = 10 -> 90; paying 30 now leaves 60 on the account.
        var response = await Buy(admin, supplier, [Line(oil, 10, 8), Line(rice, 2.5m, 4, updateCost: false)], paid: 30, reference: "INV-1001");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var purchase = await Body(response);
        Assert.StartsWith("PUR-", purchase.GetProperty("number").GetString());
        Assert.Equal(90m, purchase.GetProperty("total").GetDecimal());
        Assert.Equal(60m, purchase.GetProperty("amountDue").GetDecimal());
        Assert.Equal("received", purchase.GetProperty("status").GetString());
        Assert.Equal("INV-1001", purchase.GetProperty("supplierReference").GetString());
        Assert.Equal(2, purchase.GetProperty("items").GetArrayLength());

        Assert.Equal(15m, await StockAsync(admin, oil));
        Assert.Equal(2.5m, await StockAsync(admin, rice));
        Assert.Equal(8m, await CostAsync(admin, oil));
        Assert.Equal(1m, await CostAsync(admin, rice)); // that line asked not to touch the cost price

        Assert.Equal(60m, await BalanceAsync(admin, supplier));
        var ledger = await LedgerAsync(admin, supplier);
        Assert.Equal([("purchase", 90m), ("payment", -30m)], ledger);
        Assert.Equal(await BalanceAsync(admin, supplier), ledger.Sum(e => e.Amount));
    }

    [Fact]
    public async Task A_purchase_paid_in_full_leaves_the_balance_alone_and_writes_no_zero_entry()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier);

        Assert.Equal(HttpStatusCode.Created, (await Buy(admin, supplier, [Line(product, 2, 5)], paid: 10)).StatusCode);

        Assert.Equal(0m, await BalanceAsync(admin, supplier));
        Assert.Equal([("purchase", 10m), ("payment", -10m)], await LedgerAsync(admin, supplier));
    }

    [Fact]
    public async Task The_discount_comes_off_the_subtotal_and_the_total_is_what_the_supplier_is_owed()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier);

        var purchase = await Body(await Buy(admin, supplier, [Line(product, 3, 3.333m)], discountPercent: 10));

        // 3 x 3.333 = 9.999 -> 10.00; 10% off = 1.00; total 9.00.
        Assert.Equal(10.00m, purchase.GetProperty("subtotal").GetDecimal());
        Assert.Equal(1.00m, purchase.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(9.00m, purchase.GetProperty("total").GetDecimal());
        Assert.Equal(9.00m, await BalanceAsync(admin, supplier));
    }

    [Fact]
    public async Task Rules_that_are_broken_change_nothing()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var inactive = await CreateSupplierAsync(admin, active: false);
        var product = await CreateProductAsync(admin, supplier, stock: 5m);

        await AssertProblem(await Buy(admin, supplier, [Line(product, 1, 10)], paid: 11), HttpStatusCode.BadRequest, "paid_exceeds_total");
        await AssertProblem(await Buy(admin, supplier, [Line(product, 1, 1), Line(product, 2, 1)]), HttpStatusCode.BadRequest, "duplicate_product");
        await AssertProblem(await Buy(admin, supplier, [Line(product, 1, 1), Line(int.MaxValue - 1, 1, 1)]), HttpStatusCode.BadRequest, "unknown_product");
        await AssertProblem(await Buy(admin, inactive, [Line(product, 1, 1)]), HttpStatusCode.BadRequest, "inactive_supplier");
        await AssertProblem(await Buy(admin, int.MaxValue - 1, [Line(product, 1, 1)]), HttpStatusCode.BadRequest, "unknown_supplier");
        await AssertProblem(await Buy(admin, supplier, [Line(product, 1, 1)], purchaseDate: DateTime.Now.AddDays(3).ToString("yyyy-MM-dd")), HttpStatusCode.BadRequest, "future_date");
        await AssertProblem(await Buy(admin, supplier, []), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await Buy(admin, supplier, [Line(product, 0, 1)]), HttpStatusCode.BadRequest, "validation_failed");

        Assert.Equal(5m, await StockAsync(admin, product));
        Assert.Equal(0m, await BalanceAsync(admin, supplier));
        Assert.Empty(await LedgerAsync(admin, supplier));
    }

    // ---- the cash drawer -------------------------------------------------------------------------------------

    private static async Task<JsonElement?> OpenSessionAsync(HttpClient client)
    {
        var current = (await Body(await client.GetAsync("/api/v1/cash-drawer/current"))).GetProperty("session");
        return current.ValueKind == JsonValueKind.Null ? null : current;
    }

    /// <summary>The admin's browser with no open drawer: an earlier test of the run may have left one.</summary>
    private async Task<HttpClient> AdminWithClosedDrawerAsync()
    {
        var admin = await Admin();
        if (await OpenSessionAsync(admin) is { } open)
            Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/cash-drawer/sessions/{open.GetProperty("id").GetInt32()}/close", new { actualCash = 0m }))).StatusCode);
        return admin;
    }

    [Fact]
    public async Task Paying_from_the_drawer_takes_the_amount_off_the_expected_cash_as_a_cash_drop()
    {
        var admin = await AdminWithClosedDrawerAsync();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier);
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/cash-drawer/open", new { openingCash = 100m }))).StatusCode);
        try
        {
            var before = (await OpenSessionAsync(admin))!.Value;
            var expectedBefore = before.GetProperty("expectedCash").GetDecimal();
            var dropsBefore = before.GetProperty("cashDrops").GetArrayLength();

            var response = await Buy(admin, supplier, [Line(product, 4, 10)], paid: 25, payFromDrawer: true);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var purchase = await Body(response);
            Assert.True(purchase.GetProperty("paidFromDrawer").GetBoolean());
            var after = (await OpenSessionAsync(admin))!.Value;
            Assert.Equal(expectedBefore - 25m, after.GetProperty("expectedCash").GetDecimal());
            Assert.Equal(dropsBefore + 1, after.GetProperty("cashDrops").GetArrayLength());
            var drop = after.GetProperty("cashDrops").EnumerateArray().Single(d => d.GetProperty("reason").GetString() == $"Supplier payment {purchase.GetProperty("number").GetString()}");
            Assert.Equal(25m, drop.GetProperty("amount").GetDecimal());
            Assert.Equal(15m, await BalanceAsync(admin, supplier)); // 40 bought, 25 paid
        }
        finally
        {
            var id = (await OpenSessionAsync(admin))!.Value.GetProperty("id").GetInt32();
            await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/cash-drawer/sessions/{id}/close", new { actualCash = 0m }));
        }
    }

    [Fact]
    public async Task A_payment_that_does_not_ask_for_the_drawer_leaves_it_alone()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier);
        var session = await OpenSessionAsync(admin);
        var expectedBefore = session?.GetProperty("expectedCash").GetDecimal();

        var purchase = await Body(await Buy(admin, supplier, [Line(product, 1, 10)], paid: 10));

        Assert.False(purchase.GetProperty("paidFromDrawer").GetBoolean());
        Assert.Equal(expectedBefore, (await OpenSessionAsync(admin))?.GetProperty("expectedCash").GetDecimal());
    }

    [Fact]
    public async Task Paying_from_a_drawer_that_is_not_open_is_refused_and_nothing_is_saved()
    {
        var admin = await AdminWithClosedDrawerAsync();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier, stock: 5m);

        await AssertProblem(await Buy(admin, supplier, [Line(product, 3, 10)], paid: 10, payFromDrawer: true), HttpStatusCode.Conflict, "drawer_not_open");

        Assert.Equal(5m, await StockAsync(admin, product));
        Assert.Equal(0m, await BalanceAsync(admin, supplier));
        Assert.Empty(await LedgerAsync(admin, supplier));
    }

    [Fact]
    public async Task Nothing_is_taken_from_the_drawer_when_nothing_was_paid()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier);

        // Nothing paid: no drawer is needed, whether or not one is open.
        var response = await Buy(admin, supplier, [Line(product, 1, 10)], paid: 0, payFromDrawer: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False((await Body(response)).GetProperty("paidFromDrawer").GetBoolean());
    }

    // ---- cancelling --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cancelling_takes_the_stock_back_and_removes_the_purchase_from_the_balance()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier, stock: 5m);
        var id = (await Body(await Buy(admin, supplier, [Line(product, 10, 2)], paid: 5))).GetProperty("id").GetInt32();
        Assert.Equal(15m, await StockAsync(admin, product));
        Assert.Equal(15m, await BalanceAsync(admin, supplier)); // 20 bought, 5 paid

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/purchases/{id}/cancel", new { reason = "Wrong supplier" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await Body(response);
        Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("Wrong supplier", cancelled.GetProperty("cancelReason").GetString());
        Assert.Equal(5m, await StockAsync(admin, product));
        // The goods are undone, the 5 that was paid stays a real payment: the supplier now owes the shop 5.
        Assert.Equal(-5m, await BalanceAsync(admin, supplier));
        Assert.Equal(await BalanceAsync(admin, supplier), (await LedgerAsync(admin, supplier)).Sum(e => e.Amount));
    }

    [Fact]
    public async Task A_purchase_can_only_be_cancelled_once()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier, stock: 0m);
        var id = (await Body(await Buy(admin, supplier, [Line(product, 4, 2)]))).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/purchases/{id}/cancel", new { reason = "x" }))).StatusCode);
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/purchases/{id}/cancel", new { reason = "x" })), HttpStatusCode.Conflict, "already_cancelled");

        Assert.Equal(0m, await StockAsync(admin, product));
        Assert.Equal(0m, await BalanceAsync(admin, supplier));
    }

    [Fact]
    public async Task Cancelling_is_refused_when_part_of_the_goods_was_already_sold_and_nothing_moves()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var first = await CreateProductAsync(admin, supplier, stock: 0m);
        var second = await CreateProductAsync(admin, supplier, stock: 0m);
        var id = (await Body(await Buy(admin, supplier, [Line(first, 5, 2), Line(second, 5, 2)]))).GetProperty("id").GetInt32();

        // Four of the second product leave the shop (a stock removal stands in for a sale).
        var removal = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/{second}/adjust", new { type = "remove", quantity = 4, reason = "sold" }));
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/purchases/{id}/cancel", new { reason = "oops" })), HttpStatusCode.Conflict, "stock_already_sold");

        // All or nothing: the first product's stock and the supplier's balance are exactly as the purchase left them.
        Assert.Equal(5m, await StockAsync(admin, first));
        Assert.Equal(1m, await StockAsync(admin, second));
        Assert.Equal(20m, await BalanceAsync(admin, supplier));
        Assert.Equal("received", (await Body(await admin.GetAsync($"/api/v1/purchases/{id}"))).GetProperty("status").GetString());
    }

    // ---- reading -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_filters_by_supplier_status_and_search_and_the_summary_counts_per_currency()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var other = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier, stock: 0m);
        var otherProduct = await CreateProductAsync(admin, other, stock: 0m);
        var keep = (await Body(await Buy(admin, supplier, [Line(product, 1, 10)], paid: 4, reference: "REF-KEEP"))).GetProperty("id").GetInt32();
        var drop = (await Body(await Buy(admin, supplier, [Line(product, 1, 20)]))).GetProperty("id").GetInt32();
        await Buy(admin, other, [Line(otherProduct, 1, 99)]);
        await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/purchases/{drop}/cancel", new { reason = "no" }));

        var all = await Body(await admin.GetAsync($"/api/v1/purchases?supplierId={supplier}"));
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Equal(drop, all.GetProperty("items")[0].GetProperty("id").GetInt32()); // newest first

        var received = await Body(await admin.GetAsync($"/api/v1/purchases?supplierId={supplier}&status=received"));
        var row = Assert.Single(received.GetProperty("items").EnumerateArray());
        Assert.Equal(keep, row.GetProperty("id").GetInt32());
        Assert.Equal(6m, row.GetProperty("amountDue").GetDecimal());

        var found = await Body(await admin.GetAsync("/api/v1/purchases?q=REF-KEEP"));
        Assert.Equal(keep, Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());

        var summary = await Body(await admin.GetAsync($"/api/v1/purchases/summary?supplierId={supplier}"));
        Assert.Equal(1, summary.GetProperty("count").GetInt32());
        Assert.Equal(1, summary.GetProperty("cancelledCount").GetInt32());
        var currency = Assert.Single(summary.GetProperty("totals").EnumerateArray());
        Assert.Equal(10m, currency.GetProperty("total").GetDecimal()); // the cancelled one is not counted
        Assert.Equal(6m, currency.GetProperty("due").GetDecimal());
    }

    [Fact]
    public async Task An_unknown_purchase_is_404_everywhere()
    {
        var admin = await Admin();

        await AssertProblem(await admin.GetAsync("/api/v1/purchases/99999999"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.GetAsync("/api/v1/purchases/99999999/pdf"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/purchases/99999999/print")), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/purchases/99999999/cancel", new { reason = "x" })), HttpStatusCode.NotFound, "not_found");
    }

    // ---- paper -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_receipt_is_a_pdf_and_printing_never_fails_the_request()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier);
        var id = (await Body(await Buy(admin, supplier, [Line(product, 2, 3)]))).GetProperty("id").GetInt32();

        var pdf = await admin.GetAsync($"/api/v1/purchases/{id}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync()).Take(4).ToArray()));

        var print = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/purchases/{id}/print"));
        Assert.Equal(HttpStatusCode.OK, print.StatusCode);
        Assert.Equal($"/api/v1/purchases/{id}/pdf", (await Body(print)).GetProperty("pdfUrl").GetString());
    }

    [Fact]
    public async Task The_csv_has_one_row_per_product_line()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var a = await CreateProductAsync(admin, supplier);
        var b = await CreateProductAsync(admin, supplier);
        await Buy(admin, supplier, [Line(a, 1, 1), Line(b, 1, 1)]);

        var csv = await admin.GetAsync($"/api/v1/purchases/export?supplierId={supplier}");

        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var lines = (await csv.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); // header + two product lines
    }

    // ---- audit -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Receiving_and_cancelling_leave_audit_entries()
    {
        var admin = await Admin();
        var supplier = await CreateSupplierAsync(admin);
        var product = await CreateProductAsync(admin, supplier, stock: 0m);
        var id = (await Body(await Buy(admin, supplier, [Line(product, 1, 1)]))).GetProperty("id").GetInt32();
        await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/purchases/{id}/cancel", new { reason = "x" }));

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditTrail>().FlushAsync();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var (received, _) = await audit.SearchAsync(new AuditLogFilter { Action = AuditActions.PurchaseReceived, EntityId = id }, 0, 10);
        var (cancelled, _) = await audit.SearchAsync(new AuditLogFilter { Action = AuditActions.PurchaseCancelled, EntityId = id }, 0, 10);

        Assert.Equal("Purchase", Assert.Single(received).EntityType);
        Assert.Single(cancelled);
    }
}
