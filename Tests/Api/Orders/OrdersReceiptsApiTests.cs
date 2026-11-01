using System.Net;
using System.Text;
using System.Text.Json;
using Application.IServices.Sales;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Infrastructure;
using Xunit;
using static Tests.Api.Pos.PosTestKit;

namespace Tests.Api.Orders;

/// <summary>
/// Track S1 (D10): the Orders and Receipts APIs through the real pipeline and a real database. Every test makes its own sale through the till's
/// checkout (the same path the shop uses) and then looks at it from the back office: the lists and their filters, the permission matrix
/// (Admin vs the Cashier, who may view orders but not receipts and may change nothing), and above all the reversal - cancelling an order or voiding
/// a receipt must put the stock back, take a debt sale off the customer's account and stop the open drawer expecting the money.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OrdersReceiptsApiTests
{
    private const int Usd = 1, Lbp = 3;
    private const int Cash = 1, Card = 2;

    private readonly ApiFactory _factory;

    public OrdersReceiptsApiTests(ApiFactory factory) => _factory = factory;

    private WebApplicationFactory<Program> Host => Selling(_factory);

    private async Task<(HttpClient Browser, int UserId)> Admin() => await SignedIn(Host, ApiFactory.AdminUser, ApiFactory.AdminPin);

    private Task<(HttpClient Browser, int UserId)> CashierBrowser() => Cashier(Host);

    /// <summary>The sale of one test: what the till answered.</summary>
    private sealed record Sale(int OrderId, string OrderNumber, int? ReceiptId, string? ReceiptNumber, decimal Total, int ProductId, int Quantity);

    /// <summary>
    /// Products 37-60 are not used by the POS tests; the first piece-sold one with room for a few more sales is the next test's. Looked up on every call
    /// (a sale takes stock, so a product that ran low hands over to the next one) and stable between a test's 'before' read and its sale.
    /// </summary>
    private static async Task<int> PieceProduct(HttpClient browser)
    {
        for (var id = 37; id <= 60; id++)
        {
            var response = await browser.GetAsync($"/api/v1/pos/products/{id}");
            if (response.StatusCode != HttpStatusCode.OK) continue;
            var product = await Body(response);
            if (product.GetProperty("unitKind").GetString() == "count" && !product.GetProperty("isPack").GetBoolean() && product.GetProperty("stockQuantity").GetDecimal() >= 12)
                return id;
        }

        throw new InvalidOperationException("The demo database has no piece product with enough stock between ids 37 and 60.");
    }

    private async Task<Sale> Sell(HttpClient browser, int quantity = 2, int currencyId = Usd, int paymentMethodId = Cash, int? customerId = null, bool onAccount = false,
        decimal? discountPercentage = null, decimal rounding = 0m, string? notes = null)
    {
        var product = await PieceProduct(browser);
        var cart = Cart(currencyId, [Line(product, quantity)], discountPercentage: discountPercentage);
        var total = (await Price(browser, cart)).GetProperty("total").GetDecimal() + rounding;
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/sales/checkout",
            Checkout(cart, total, onAccount ? 0 : paymentMethodId, customerId, onAccount, notes: notes)));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        var sale = JsonDocument.Parse(text).RootElement;
        return new Sale(
            sale.GetProperty("orderId").GetInt32(),
            $"ORD-{sale.GetProperty("orderId").GetInt32():D6}", // the Orders page's number; the checkout answer adds the date
            sale.GetProperty("receiptId").ValueKind == JsonValueKind.Null ? null : sale.GetProperty("receiptId").GetInt32(),
            sale.TryGetProperty("receiptNumber", out var number) && number.ValueKind == JsonValueKind.String ? number.GetString() : null,
            sale.GetProperty("total").GetDecimal(),
            product,
            quantity);
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>A fresh scope (and DbContext) per call: a scoped context keeps the orders it loaded the first time, which would hide the reversal.</summary>
    private async Task<T> WithDrawers<T>(Func<ICashDrawerService, Task<T>> action)
    {
        using var scope = Host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICashDrawerService>());
    }

    private static Task<HttpResponseMessage> SetStatus(HttpClient browser, int orderId, int status)
        => browser.SendAsync(Json(HttpMethod.Put, $"/api/v1/orders/{orderId}/status", new { status }));

    private static Task<HttpResponseMessage> Void(HttpClient browser, int receiptId, string? reason = "Customer changed their mind")
        => browser.SendAsync(Json(HttpMethod.Post, $"/api/v1/receipts/{receiptId}/void", new { reason }));

    private static async Task<JsonElement> FindOrder(HttpClient browser, string orderNumber, string extraQuery = "")
    {
        var page = await Ok(await browser.GetAsync($"/api/v1/orders?q={orderNumber}{extraQuery}"));
        return page.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("orderNumber").GetString() == orderNumber);
    }

    // ---- the permission matrix ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_callers_are_turned_away_from_every_orders_and_receipts_endpoint()
    {
        var anonymous = _factory.CreateBrowser();

        foreach (var url in new[] { "/api/v1/orders", "/api/v1/orders/summary", "/api/v1/orders/1", "/api/v1/orders/export", "/api/v1/receipts", "/api/v1/receipts/stats", "/api/v1/receipts/1", "/api/v1/receipts/1/pdf" })
            await AssertProblem(await anonymous.GetAsync(url), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_may_look_at_orders_but_change_nothing_and_sees_no_receipts()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin);
        var (cashier, _) = await CashierBrowser();

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/orders/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync($"/api/v1/orders/{sale.OrderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/orders/export")).StatusCode);

        await AssertProblem(await SetStatus(cashier, sale.OrderId, 2), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.GetAsync("/api/v1/receipts"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.GetAsync("/api/v1/receipts/stats"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.GetAsync($"/api/v1/receipts/{sale.ReceiptId}"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await Void(cashier, sale.ReceiptId!.Value), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"/api/v1/receipts/{sale.ReceiptId}/print")), HttpStatusCode.Forbidden, "forbidden");

        // ... and nothing the cashier tried changed the sale.
        var order = await Ok(await admin.GetAsync($"/api/v1/orders/{sale.OrderId}"));
        Assert.Equal(3, order.GetProperty("status").GetInt32());
        Assert.False((await Ok(await admin.GetAsync($"/api/v1/receipts/{sale.ReceiptId}"))).GetProperty("isVoid").GetBoolean());
    }

    [Fact]
    public async Task Changing_a_status_needs_the_header_the_whole_api_needs()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin);

        var noHeader = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/orders/{sale.OrderId}/status", new { status = 1 }, clientHeader: false));

        await AssertProblem(noHeader, HttpStatusCode.Forbidden, "client_header_required");
    }

    // ---- orders: list, filters, summary, details -----------------------------------------------------------------------

    [Fact]
    public async Task A_sale_shows_up_in_the_orders_list_with_its_number_customer_currency_and_status()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin, quantity: 3, customerId: 9, paymentMethodId: Card);

        var row = await FindOrder(admin, sale.OrderNumber);

        Assert.Equal(sale.OrderId, row.GetProperty("id").GetInt32());
        Assert.Equal($"ORD-{sale.OrderId:D6}", row.GetProperty("orderNumber").GetString());
        Assert.Equal("Elias Sfeir", row.GetProperty("customerName").GetString());
        Assert.Equal(9, row.GetProperty("customerId").GetInt32());
        Assert.Equal(sale.Total, row.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(Usd, row.GetProperty("currencyId").GetInt32());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("currencyNotation").GetString()));
        Assert.Equal(1, row.GetProperty("itemCount").GetInt32());
        Assert.EndsWith("Z", row.GetProperty("orderDate").GetString());
        Assert.Equal(3, row.GetProperty("status").GetInt32()); // a till sale is born Delivered
    }

    [Fact]
    public async Task A_walk_in_sale_has_no_customer()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin);

        var row = await FindOrder(admin, sale.OrderNumber);

        Assert.Equal(JsonValueKind.Null, row.GetProperty("customerId").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("customerName").ValueKind);
    }

    [Fact]
    public async Task The_list_is_newest_first_and_searches_by_number_digits_customer_and_total()
    {
        var (admin, _) = await Admin();
        var older = await Sell(admin);
        var newer = await Sell(admin, customerId: 9);

        var newest = await Ok(await admin.GetAsync("/api/v1/orders?pageSize=2"));
        var byDigits = await Ok(await admin.GetAsync($"/api/v1/orders?q={newer.OrderId}"));
        var byName = await Ok(await admin.GetAsync("/api/v1/orders?q=elias%20sfeir&pageSize=200"));
        var byTotal = await Ok(await admin.GetAsync($"/api/v1/orders?q={newer.Total.ToString(System.Globalization.CultureInfo.InvariantCulture)}&pageSize=200"));

        Assert.Equal(newer.OrderId, newest.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal(older.OrderId, newest.GetProperty("items")[1].GetProperty("id").GetInt32());
        Assert.Contains(byDigits.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetInt32() == newer.OrderId);
        Assert.All(byName.GetProperty("items").EnumerateArray(), o => Assert.Equal("Elias Sfeir", o.GetProperty("customerName").GetString()));
        Assert.Contains(byName.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetInt32() == newer.OrderId);
        Assert.Contains(byTotal.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetInt32() == newer.OrderId);
    }

    [Fact]
    public async Task Paging_reports_the_total_and_sorting_follows_the_whitelist()
    {
        var (admin, _) = await Admin();
        await Sell(admin);
        await Sell(admin);

        var first = await Ok(await admin.GetAsync("/api/v1/orders?page=1&pageSize=1&sort=number&dir=desc"));
        var second = await Ok(await admin.GetAsync("/api/v1/orders?page=2&pageSize=1&sort=number&dir=desc"));
        var oldest = await Ok(await admin.GetAsync("/api/v1/orders?pageSize=1&sort=number&dir=asc"));
        var junkSort = await admin.GetAsync("/api/v1/orders?sort=password");
        var pageSizeTooBig = await admin.GetAsync("/api/v1/orders?pageSize=5000");

        Assert.True(first.GetProperty("total").GetInt32() >= 2);
        Assert.Equal(1, first.GetProperty("pageSize").GetInt32());
        Assert.True(first.GetProperty("items")[0].GetProperty("id").GetInt32() > second.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.True(oldest.GetProperty("items")[0].GetProperty("id").GetInt32() < second.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.OK, junkSort.StatusCode); // an unknown column falls back to the default order instead of reaching a query
        await AssertProblem(pageSizeTooBig, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Status_customer_and_date_filters_narrow_the_list()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin, customerId: 9);
        await SetStatus(admin, sale.OrderId, 2); // Shipped
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1)).ToString("yyyy-MM-dd");

        var shipped = await Ok(await admin.GetAsync($"/api/v1/orders?status=2&q={sale.OrderNumber}"));
        var delivered = await Ok(await admin.GetAsync($"/api/v1/orders?status=3&q={sale.OrderNumber}"));
        var ofCustomer = await Ok(await admin.GetAsync("/api/v1/orders?customerId=9&pageSize=200"));
        var otherCustomer = await Ok(await admin.GetAsync("/api/v1/orders?customerId=10&pageSize=200"));
        var todayOnly = await Ok(await admin.GetAsync($"/api/v1/orders?from={today}&to={today}&q={sale.OrderNumber}"));
        var yesterdayOnly = await Ok(await admin.GetAsync($"/api/v1/orders?from={yesterday}&to={yesterday}&q={sale.OrderNumber}"));

        Assert.Equal(1, shipped.GetProperty("total").GetInt32());
        Assert.Equal(0, delivered.GetProperty("total").GetInt32());
        Assert.Contains(ofCustomer.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetInt32() == sale.OrderId);
        Assert.DoesNotContain(otherCustomer.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetInt32() == sale.OrderId);
        Assert.Equal(1, todayOnly.GetProperty("total").GetInt32());
        Assert.Equal(0, yesterdayOnly.GetProperty("total").GetInt32());
        await AssertProblem(await admin.GetAsync("/api/v1/orders?status=9"), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.GetAsync("/api/v1/orders?from=not-a-date"), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task The_summary_counts_the_last_30_days_and_totals_revenue_per_currency_without_the_cancelled()
    {
        var (admin, _) = await Admin();
        var before = await Ok(await admin.GetAsync("/api/v1/orders/summary"));
        var usd = await Sell(admin);
        var lbp = await Sell(admin, currencyId: Lbp);
        var cancelled = await Sell(admin);
        await Ok(await SetStatus(admin, cancelled.OrderId, 4));

        var after = await Ok(await admin.GetAsync("/api/v1/orders/summary"));

        Assert.Equal(30, after.GetProperty("periodDays").GetInt32());
        Assert.Equal(before.GetProperty("totalOrders").GetInt32() + 3, after.GetProperty("totalOrders").GetInt32());
        Assert.Equal(before.GetProperty("cancelledOrders").GetInt32() + 1, after.GetProperty("cancelledOrders").GetInt32());
        Assert.Equal(before.GetProperty("deliveredOrders").GetInt32() + 2, after.GetProperty("deliveredOrders").GetInt32());

        decimal Revenue(JsonElement summary, int currencyId)
            => summary.GetProperty("revenue").EnumerateArray().Where(r => r.GetProperty("currencyId").GetInt32() == currencyId).Sum(r => r.GetProperty("amount").GetDecimal());

        Assert.Equal(Revenue(before, Usd) + usd.Total, Revenue(after, Usd)); // the cancelled sale is not revenue
        Assert.Equal(Revenue(before, Lbp) + lbp.Total, Revenue(after, Lbp)); // and LBP is its own line, never added to the dollars
        Assert.True(lbp.Total > usd.Total * 1000, "the LBP sale is in thousands: adding it to the dollars would be nonsense");
    }

    [Fact]
    public async Task The_details_show_lines_totals_the_customer_and_the_receipt()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin, quantity: 3, customerId: 9);

        var order = await Ok(await admin.GetAsync($"/api/v1/orders/{sale.OrderId}"));

        Assert.Equal(sale.OrderNumber, order.GetProperty("orderNumber").GetString());
        Assert.Equal("Elias Sfeir", order.GetProperty("customerName").GetString());
        var line = Assert.Single(order.GetProperty("lines").EnumerateArray());
        Assert.Equal(sale.ProductId, line.GetProperty("productId").GetInt32());
        Assert.Equal(3, line.GetProperty("quantity").GetInt32());
        Assert.False(string.IsNullOrEmpty(line.GetProperty("productName").GetString()));
        Assert.Equal(line.GetProperty("unitPrice").GetDecimal() * 3, line.GetProperty("totalPrice").GetDecimal());
        Assert.Equal(sale.Total, order.GetProperty("subtotal").GetDecimal());
        Assert.Equal(sale.Total, order.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(0m, order.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(0m, order.GetProperty("adjustment").GetDecimal());
        Assert.Equal(0m, order.GetProperty("taxAmount").GetDecimal());
        var receipt = Assert.Single(order.GetProperty("receipts").EnumerateArray());
        Assert.Equal(sale.ReceiptId, receipt.GetProperty("id").GetInt32());
        Assert.False(receipt.GetProperty("isVoid").GetBoolean());
    }

    [Fact]
    public async Task The_details_split_a_percentage_discount_and_a_cash_rounding_adjustment()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin, quantity: 4, discountPercentage: 10m, rounding: 0.05m);

        var order = await Ok(await admin.GetAsync($"/api/v1/orders/{sale.OrderId}"));

        var subtotal = order.GetProperty("subtotal").GetDecimal();
        Assert.Equal(10m, order.GetProperty("discountPercentage").GetDecimal());
        Assert.Equal(subtotal * 0.10m, order.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(0.05m, order.GetProperty("adjustment").GetDecimal()); // the cashier rounded the total up by five cents (rounding down would have become extra discount)
        Assert.Equal(sale.Total, order.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task An_unknown_order_is_a_404_with_a_code()
    {
        var (admin, _) = await Admin();

        await AssertProblem(await admin.GetAsync("/api/v1/orders/99999999"), HttpStatusCode.NotFound, "order_not_found");
        await AssertProblem(await SetStatus(admin, 99999999, 1), HttpStatusCode.NotFound, "order_not_found");
    }

    // ---- status changes ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_plain_status_change_changes_the_label_and_nothing_else()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin, quantity: 2);
        var stock = await StockOf(admin, sale.ProductId);

        var result = await Ok(await SetStatus(admin, sale.OrderId, 1));

        Assert.Equal(sale.OrderId, result.GetProperty("orderId").GetInt32());
        Assert.Equal(1, result.GetProperty("status").GetInt32());
        Assert.False(result.TryGetProperty("reversal", out var reversal) && reversal.ValueKind != JsonValueKind.Null);
        Assert.Equal(1, (await FindOrder(admin, sale.OrderNumber)).GetProperty("status").GetInt32());
        Assert.Equal(stock, await StockOf(admin, sale.ProductId));
        Assert.False((await Ok(await admin.GetAsync($"/api/v1/receipts/{sale.ReceiptId}"))).GetProperty("isVoid").GetBoolean());
    }

    [Fact]
    public async Task A_status_outside_the_enum_is_refused()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin);

        await AssertProblem(await SetStatus(admin, sale.OrderId, 6), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await SetStatus(admin, sale.OrderId, -1), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Cancelling_an_order_voids_its_receipt_and_puts_the_stock_back()
    {
        var (admin, _) = await Admin();
        var before = await StockOf(admin, await PieceProduct(admin));
        var sale = await Sell(admin, quantity: 3);
        Assert.Equal(before - 3, await StockOf(admin, sale.ProductId));

        var result = await Ok(await SetStatus(admin, sale.OrderId, 4));

        var reversal = result.GetProperty("reversal");
        Assert.True(reversal.GetProperty("orderCancelled").GetBoolean());
        Assert.Equal(1, reversal.GetProperty("receiptsVoided").GetInt32());
        Assert.Equal(1, reversal.GetProperty("stockLinesRestored").GetInt32());
        Assert.Equal(0m, reversal.GetProperty("debtReversed").GetDecimal());
        Assert.Equal(4, result.GetProperty("status").GetInt32());
        Assert.Equal(before, await StockOf(admin, sale.ProductId));
        Assert.Equal(4, (await Ok(await admin.GetAsync($"/api/v1/orders/{sale.OrderId}"))).GetProperty("status").GetInt32());
        var receipt = await Ok(await admin.GetAsync($"/api/v1/receipts/{sale.ReceiptId}"));
        Assert.True(receipt.GetProperty("isVoid").GetBoolean());
        Assert.False(string.IsNullOrEmpty(receipt.GetProperty("voidReason").GetString()));
    }

    [Fact]
    public async Task Cancelling_a_debt_sale_takes_it_off_the_customers_account()
    {
        var (admin, _) = await Admin();
        var balance = await BalanceOf(admin, "Elias Sfeir");
        var sale = await Sell(admin, quantity: 2, customerId: 9, onAccount: true);
        Assert.Equal(balance + sale.Total, await BalanceOf(admin, "Elias Sfeir"));

        var result = await Ok(await SetStatus(admin, sale.OrderId, 4));

        Assert.Equal(sale.Total, result.GetProperty("reversal").GetProperty("debtReversed").GetDecimal());
        Assert.Equal(balance, await BalanceOf(admin, "Elias Sfeir"));
    }

    [Fact]
    public async Task Cancelling_a_sale_stops_the_open_drawer_expecting_its_cash()
    {
        var (admin, userId) = await Admin();
        var session = await WithDrawers(d => d.OpenDrawerAsync(userId, 100m));
        try
        {
            var sale = await Sell(admin, quantity: 2);
            Assert.Equal(100m + sale.Total, await WithDrawers(d => d.CalculateExpectedCashAsync(session.Id)));

            await Ok(await SetStatus(admin, sale.OrderId, 4));

            Assert.Equal(100m, await WithDrawers(d => d.CalculateExpectedCashAsync(session.Id)));
        }
        finally
        {
            await WithDrawers(d => d.CloseDrawerAsync(session.Id, 100m));
        }
    }

    [Fact]
    public async Task A_cancelled_order_cannot_be_cancelled_again_or_reopened_and_stock_is_returned_only_once()
    {
        var (admin, _) = await Admin();
        var before = await StockOf(admin, await PieceProduct(admin));
        var sale = await Sell(admin, quantity: 2);
        await Ok(await SetStatus(admin, sale.OrderId, 4));

        await AssertProblem(await SetStatus(admin, sale.OrderId, 4), HttpStatusCode.Conflict, "order_closed");
        await AssertProblem(await SetStatus(admin, sale.OrderId, 1), HttpStatusCode.Conflict, "order_cancelled");

        Assert.Equal(before, await StockOf(admin, sale.ProductId));
        Assert.Equal(4, (await Ok(await admin.GetAsync($"/api/v1/orders/{sale.OrderId}"))).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Marking_an_order_Returned_is_only_a_label_it_does_not_restore_stock_and_a_later_cancel_is_refused()
    {
        // Pins the behaviour of the old status form: Returned moves nothing, and afterwards there is no way left to put the stock back.
        var (admin, _) = await Admin();
        var sale = await Sell(admin, quantity: 2);
        var afterSale = await StockOf(admin, sale.ProductId);

        await Ok(await SetStatus(admin, sale.OrderId, 5));

        Assert.Equal(afterSale, await StockOf(admin, sale.ProductId));
        await AssertProblem(await SetStatus(admin, sale.OrderId, 4), HttpStatusCode.Conflict, "order_closed");
        Assert.Equal(afterSale, await StockOf(admin, sale.ProductId));
    }

    // ---- export ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_export_is_a_utf8_csv_of_the_filtered_list_with_every_page()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin, customerId: 9);

        var response = await admin.GetAsync($"/api/v1/orders/export?q={sale.OrderNumber}&pageSize=1");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("orders_", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // BOM: Excel reads Arabic names correctly
        var lines = text.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("\"Order\",\"Date\",\"Customer\",\"Status\",\"Items\",\"Total\",\"Currency\"", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.Contains($"\"{sale.OrderNumber}\"", lines[1]);
        Assert.Contains("\"Elias Sfeir\"", lines[1]);
        Assert.Contains("\"Delivered\"", lines[1]);
        Assert.Contains($"\"{sale.Total.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\"", lines[1]);
    }

    [Fact]
    public async Task A_customer_named_like_a_spreadsheet_formula_is_neutralised_in_the_export()
    {
        var (admin, _) = await Admin();
        var created = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/customers", new { firstName = "=HYPERLINK(\"http://evil\")", lastName = "Test", phoneNumber = "70123456" }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var customerId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
        var sale = await Sell(admin, customerId: customerId);

        var csv = await (await admin.GetAsync($"/api/v1/orders/export?q={sale.OrderNumber}")).Content.ReadAsStringAsync();

        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\") Test\"", csv);
        Assert.DoesNotContain(",\"=HYPERLINK", csv);
    }

    // ---- receipts: list, details, stats ----------------------------------------------------------------------------

    [Fact]
    public async Task A_sale_shows_up_in_the_receipts_list_and_the_details_match_it()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin, quantity: 3, customerId: 9, paymentMethodId: Card);

        var byNumber = await Ok(await admin.GetAsync($"/api/v1/receipts?q={Uri.EscapeDataString(sale.ReceiptNumber!)}"));
        var row = Assert.Single(byNumber.GetProperty("items").EnumerateArray());
        var receipt = await Ok(await admin.GetAsync($"/api/v1/receipts/{sale.ReceiptId}"));

        Assert.Equal(sale.ReceiptNumber, receipt.GetProperty("receiptNumber").GetString());
        Assert.Equal(sale.ReceiptId, row.GetProperty("id").GetInt32());
        Assert.Equal(sale.OrderId, row.GetProperty("orderId").GetInt32());
        Assert.Equal("Elias Sfeir", row.GetProperty("customerName").GetString());
        Assert.Equal("Card", row.GetProperty("paymentMethod").GetString());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("cashierName").GetString()));
        Assert.Equal(sale.Total, row.GetProperty("totalAmount").GetDecimal());
        Assert.False(row.GetProperty("isVoid").GetBoolean());
        Assert.Equal(sale.Total, receipt.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(sale.Total, receipt.GetProperty("amountPaid").GetDecimal());
        Assert.True(receipt.GetProperty("isFullyPaid").GetBoolean());
        var line = Assert.Single(receipt.GetProperty("lines").EnumerateArray());
        Assert.Equal(3m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(sale.Total, line.GetProperty("lineTotal").GetDecimal());
        var payment = Assert.Single(receipt.GetProperty("payments").EnumerateArray());
        Assert.Equal("Card", payment.GetProperty("paymentMethod").GetString());
        Assert.Equal(sale.Total, payment.GetProperty("amount").GetDecimal());
        Assert.False(receipt.GetProperty("isVoid").GetBoolean());
    }

    [Fact]
    public async Task The_receipts_list_filters_by_status_and_date_and_sorts_by_the_whitelist()
    {
        var (admin, _) = await Admin();
        var kept = await Sell(admin);
        var voided = await Sell(admin);
        await Ok(await Void(admin, voided.ReceiptId!.Value));
        var keptNumber = kept.ReceiptNumber;
        var voidedNumber = voided.ReceiptNumber;
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1)).ToString("yyyy-MM-dd");

        async Task<int> Count(string query) => (await Ok(await admin.GetAsync($"/api/v1/receipts?{query}"))).GetProperty("total").GetInt32();

        Assert.Equal(1, await Count($"status=valid&q={keptNumber}"));
        Assert.Equal(0, await Count($"status=valid&q={voidedNumber}"));
        Assert.Equal(1, await Count($"status=void&q={voidedNumber}"));
        Assert.Equal(0, await Count($"status=void&q={keptNumber}"));
        Assert.Equal(1, await Count($"q={keptNumber}&from={today}&to={today}"));
        Assert.Equal(0, await Count($"q={keptNumber}&from={yesterday}&to={yesterday}"));

        var junk = await admin.GetAsync("/api/v1/receipts?sort=password");
        var byAmount = await Ok(await admin.GetAsync("/api/v1/receipts?sort=amount&dir=desc&pageSize=5"));
        var amounts = byAmount.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("totalAmount").GetDecimal()).ToList();
        Assert.Equal(HttpStatusCode.OK, junk.StatusCode);
        Assert.Equal(amounts.OrderByDescending(a => a), amounts);
    }

    [Fact]
    public async Task An_unknown_receipt_is_a_404_with_a_code()
    {
        var (admin, _) = await Admin();

        await AssertProblem(await admin.GetAsync("/api/v1/receipts/99999999"), HttpStatusCode.NotFound, "receipt_not_found");
        await AssertProblem(await Void(admin, 99999999), HttpStatusCode.NotFound, "receipt_not_found");
        await AssertProblem(await admin.GetAsync("/api/v1/receipts/99999999/pdf"), HttpStatusCode.NotFound, "receipt_not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/receipts/99999999/print")), HttpStatusCode.NotFound, "receipt_not_found");
    }

    [Fact]
    public async Task Stats_total_each_currency_on_its_own_count_voids_and_break_payments_down()
    {
        var (admin, _) = await Admin();
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        var before = await Ok(await admin.GetAsync($"/api/v1/receipts/stats?from={today}&to={today}"));
        var cash = await Sell(admin);
        var card = await Sell(admin, paymentMethodId: Card);
        var lbp = await Sell(admin, currencyId: Lbp);
        var voided = await Sell(admin);
        await Ok(await Void(admin, voided.ReceiptId!.Value));

        var after = await Ok(await admin.GetAsync($"/api/v1/receipts/stats?from={today}&to={today}"));

        Assert.Equal(today, after.GetProperty("from").GetString());
        Assert.Equal(today, after.GetProperty("to").GetString());
        Assert.Equal(before.GetProperty("validCount").GetInt32() + 3, after.GetProperty("validCount").GetInt32());
        Assert.Equal(before.GetProperty("voidCount").GetInt32() + 1, after.GetProperty("voidCount").GetInt32());

        decimal Sales(JsonElement stats, int currencyId)
            => stats.GetProperty("currencies").EnumerateArray().Where(c => c.GetProperty("currencyId").GetInt32() == currencyId).Sum(c => c.GetProperty("totalSales").GetDecimal());

        Assert.Equal(Sales(before, Usd) + cash.Total + card.Total, Sales(after, Usd)); // the voided sale is not in the sales
        Assert.Equal(Sales(before, Lbp) + lbp.Total, Sales(after, Lbp));
        var usd = after.GetProperty("currencies").EnumerateArray().Single(c => c.GetProperty("currencyId").GetInt32() == Usd);
        var day = Assert.Single(usd.GetProperty("daily").EnumerateArray());
        Assert.Equal(today, day.GetProperty("date").GetString());
        Assert.Equal(usd.GetProperty("totalSales").GetDecimal(), day.GetProperty("amount").GetDecimal());
        Assert.Equal(usd.GetProperty("totalSales").GetDecimal() / usd.GetProperty("receiptCount").GetInt32(), usd.GetProperty("averageReceipt").GetDecimal());

        int Paid(JsonElement stats, string method)
            => stats.GetProperty("paymentMethods").EnumerateArray().Where(p => p.GetProperty("paymentMethod").GetString() == method).Sum(p => p.GetProperty("count").GetInt32());

        Assert.Equal(Paid(before, "Cash") + 2, Paid(after, "Cash")); // the USD and the LBP sale
        Assert.Equal(Paid(before, "Card") + 1, Paid(after, "Card"));
    }

    [Fact]
    public async Task Stats_without_a_range_cover_the_last_30_days_up_to_today()
    {
        var (admin, _) = await Admin();

        var stats = await Ok(await admin.GetAsync("/api/v1/receipts/stats"));

        Assert.Equal(DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"), stats.GetProperty("to").GetString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(-30)).ToString("yyyy-MM-dd"), stats.GetProperty("from").GetString());
    }

    // ---- voiding a receipt ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Voiding_a_receipt_needs_a_reason()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin);

        await AssertProblem(await Void(admin, sale.ReceiptId!.Value, reason: null), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await Void(admin, sale.ReceiptId!.Value, reason: ""), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await Void(admin, sale.ReceiptId!.Value, reason: new string('x', 501)), HttpStatusCode.BadRequest, "validation_failed");

        Assert.False((await Ok(await admin.GetAsync($"/api/v1/receipts/{sale.ReceiptId}"))).GetProperty("isVoid").GetBoolean());
    }

    [Fact]
    public async Task Voiding_a_receipt_reverses_the_whole_sale()
    {
        var (admin, userId) = await Admin();
        var before = await StockOf(admin, await PieceProduct(admin));
        var balance = await BalanceOf(admin, "Elias Sfeir");
        var sale = await Sell(admin, quantity: 4, customerId: 9, onAccount: true);

        var result = await Ok(await Void(admin, sale.ReceiptId!.Value, "  Wrong customer  "));

        Assert.True(result.GetProperty("orderCancelled").GetBoolean());
        Assert.Equal(1, result.GetProperty("receiptsVoided").GetInt32());
        Assert.Equal(1, result.GetProperty("stockLinesRestored").GetInt32());
        Assert.Equal(sale.Total, result.GetProperty("debtReversed").GetDecimal());
        Assert.Equal(before, await StockOf(admin, sale.ProductId));
        Assert.Equal(balance, await BalanceOf(admin, "Elias Sfeir"));

        var receipt = await Ok(await admin.GetAsync($"/api/v1/receipts/{sale.ReceiptId}"));
        Assert.True(receipt.GetProperty("isVoid").GetBoolean());
        Assert.Equal("Wrong customer", receipt.GetProperty("voidReason").GetString()); // trimmed
        Assert.False(string.IsNullOrEmpty(receipt.GetProperty("voidedBy").GetString()));
        Assert.NotEqual(JsonValueKind.Null, receipt.GetProperty("voidDate").ValueKind);
        Assert.Equal(4, (await Ok(await admin.GetAsync($"/api/v1/orders/{sale.OrderId}"))).GetProperty("status").GetInt32());

        var row = (await Ok(await admin.GetAsync($"/api/v1/receipts?status=void&q={Uri.EscapeDataString(receipt.GetProperty("receiptNumber").GetString()!)}"))).GetProperty("items")[0];
        Assert.True(row.GetProperty("isVoid").GetBoolean());
        Assert.Equal("Wrong customer", row.GetProperty("voidReason").GetString());
        Assert.Equal(receipt.GetProperty("voidedBy").GetString(), row.GetProperty("voidedBy").GetString());
        _ = userId;
    }

    [Fact]
    public async Task Voiding_a_sale_stops_the_open_drawer_expecting_its_cash()
    {
        var (admin, userId) = await Admin();
        var session = await WithDrawers(d => d.OpenDrawerAsync(userId, 50m));
        try
        {
            var sale = await Sell(admin, quantity: 2);
            Assert.Equal(50m + sale.Total, await WithDrawers(d => d.CalculateExpectedCashAsync(session.Id)));

            await Ok(await Void(admin, sale.ReceiptId!.Value));

            Assert.Equal(50m, await WithDrawers(d => d.CalculateExpectedCashAsync(session.Id)));
        }
        finally
        {
            await WithDrawers(d => d.CloseDrawerAsync(session.Id, 50m));
        }
    }

    [Fact]
    public async Task A_receipt_cannot_be_voided_twice_and_the_stock_is_returned_once()
    {
        var (admin, _) = await Admin();
        var before = await StockOf(admin, await PieceProduct(admin));
        var sale = await Sell(admin, quantity: 2);
        await Ok(await Void(admin, sale.ReceiptId!.Value));

        await AssertProblem(await Void(admin, sale.ReceiptId!.Value), HttpStatusCode.Conflict, "receipt_already_void");

        Assert.Equal(before, await StockOf(admin, sale.ProductId));
    }

    [Fact]
    public async Task Cancelling_the_order_after_the_receipt_was_voided_is_refused_and_the_other_way_round()
    {
        var (admin, _) = await Admin();
        var voidedFirst = await Sell(admin);
        await Ok(await Void(admin, voidedFirst.ReceiptId!.Value));
        var cancelledFirst = await Sell(admin);
        await Ok(await SetStatus(admin, cancelledFirst.OrderId, 4));

        await AssertProblem(await SetStatus(admin, voidedFirst.OrderId, 4), HttpStatusCode.Conflict, "order_closed");
        await AssertProblem(await Void(admin, cancelledFirst.ReceiptId!.Value), HttpStatusCode.Conflict, "receipt_already_void");
    }

    // ---- printing --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_reprint_answers_even_when_no_printer_is_there_and_the_pdf_is_a_pdf()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin);

        var print = await Ok(await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/receipts/{sale.ReceiptId}/print")));
        var pdf = await admin.GetAsync($"/api/v1/receipts/{sale.ReceiptId}/pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync();

        Assert.True(print.TryGetProperty("printed", out _));
        Assert.False(string.IsNullOrEmpty(print.GetProperty("message").GetString()));
        Assert.Equal($"/api/v1/receipts/{sale.ReceiptId}/pdf", print.GetProperty("pdfUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes[..4]));
    }

    [Fact]
    public async Task Messages_of_a_refusal_come_back_in_the_language_of_the_request()
    {
        var (admin, _) = await Admin();
        var sale = await Sell(admin);
        await Ok(await SetStatus(admin, sale.OrderId, 4));

        var english = await Body(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/orders/{sale.OrderId}/status", new { status = 4 }, language: "en")));
        var arabic = await Body(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/orders/{sale.OrderId}/status", new { status = 4 }, language: "ar")));

        Assert.Contains("already been cancelled or returned", english.GetProperty("detail").GetString());
        Assert.Contains(sale.OrderNumber, english.GetProperty("detail").GetString());
        Assert.Contains("ملغى", arabic.GetProperty("detail").GetString());
        Assert.Contains(sale.OrderNumber, arabic.GetProperty("detail").GetString());
    }
}
