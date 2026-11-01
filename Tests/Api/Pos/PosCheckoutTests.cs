using System.Net;
using Application.IServices.Sales;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Infrastructure;
using Xunit;
using static Tests.Api.Pos.PosTestKit;

namespace Tests.Api.Pos;

/// <summary>
/// D10 for the till: the money paths of <c>POST api/v1/pos/sales/checkout</c> and the held-cart endpoints, through the real pipeline and a real database -
/// totals with units / tiers / discounts / currencies, idempotent replay, stock, the drawer, customer debt, and "a refused sale leaves nothing behind".
/// Each test sells its own product so the shared demo database stays predictable. The shared host has no licence (selling is stopped);
/// <see cref="PosTestKit.Selling"/> is the same app with a licence that allows it.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PosCheckoutTests
{
    private const int Usd = 1, Eur = 2, Lbp = 3;
    private const int Cash = 1, Card = 2;

    private readonly ApiFactory _factory;

    public PosCheckoutTests(ApiFactory factory) => _factory = factory;

    private static async Task<HttpResponseMessage> Sell(HttpClient till, object request)
        => await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/sales/checkout", request));

    private static async Task<System.Text.Json.JsonElement> Sold(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return System.Text.Json.JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<(HttpClient Till, int UserId)> Till() => await Cashier(Selling(_factory));

    // ---- the happy path ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_cash_sale_creates_the_order_and_the_receipt_and_takes_the_stock()
    {
        var (till, _) = await Till();
        var batteries = await StockOf(till, 48);
        var cheese = await StockOf(till, 17);
        var cart = Cart(Usd, [Line(48, quantity: 2), Line(17, weightOrVolume: 0.5m)]); // 6.40 + 5.75

        var sale = await Sold(await Sell(till, Checkout(cart, 12.15m, Cash)));

        Assert.StartsWith("ORD-", sale.GetProperty("orderNumber").GetString());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, sale.GetProperty("receiptId").ValueKind);
        Assert.Equal(12.15m, sale.GetProperty("total").GetDecimal());
        Assert.Equal(12.15m, sale.GetProperty("subtotal").GetDecimal());
        Assert.Equal(0m, sale.GetProperty("discountAmount").GetDecimal());
        Assert.Equal("Cash", sale.GetProperty("paymentMethod").GetString());
        Assert.False(sale.GetProperty("replayed").GetBoolean());
        Assert.False(sale.GetProperty("addedToDebt").GetBoolean());
        Assert.Equal(2, sale.GetProperty("lines").GetArrayLength());
        Assert.Equal(batteries - 2, await StockOf(till, 48));
        Assert.Equal(cheese - 0.5m, await StockOf(till, 17));
    }

    [Fact]
    public async Task The_receipt_row_carries_the_discount_a_lowered_total_became()
    {
        var (till, _) = await Till();
        var cart = Cart(Usd, [Line(7, quantity: 2)], discountPercentage: 10m); // Arabic Coffee 2 x 6.50 = 13.00, -10% = 11.70

        var sale = await Sold(await Sell(till, Checkout(cart, 11.20m, Cash))); // cashier rounds down another 0.50

        Assert.Equal(13m, sale.GetProperty("subtotal").GetDecimal());
        Assert.Equal(1.8m, sale.GetProperty("discountAmount").GetDecimal()); // 1.30 + the 0.50 the cashier took off
        Assert.Equal(11.2m, sale.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_total_slightly_above_the_priced_one_is_cash_rounding_and_is_kept()
    {
        var (till, _) = await Till();

        var sale = await Sold(await Sell(till, Checkout(Cart(Usd, [Line(32)]), 5.50m, Cash))); // 5.40 priced, 0.10 up

        Assert.Equal(5.5m, sale.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_total_far_from_the_priced_one_is_refused_before_anything_is_written()
    {
        var (till, _) = await Till();
        var before = await StockOf(till, 36);

        var tooLow = await Sell(till, Checkout(Cart(Usd, [Line(36, quantity: 4)]), 0.10m, Cash));
        var tooHigh = await Sell(till, Checkout(Cart(Usd, [Line(36, quantity: 4)]), 999m, Cash));

        await AssertProblem(tooLow, HttpStatusCode.Conflict, "total_mismatch");
        await AssertProblem(tooHigh, HttpStatusCode.Conflict, "total_mismatch");
        Assert.Equal(before, await StockOf(till, 36));
    }

    // ---- idempotency --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Repeating_a_checkout_with_the_same_key_returns_the_same_sale_and_sells_once()
    {
        var (till, _) = await Till();
        var before = await StockOf(till, 11);
        var request = Checkout(Cart(Usd, [Line(11, quantity: 3)]), (await Price(till, Cart(Usd, [Line(11, quantity: 3)]))).GetProperty("total").GetDecimal(), Cash, key: "retry-after-lost-response");

        var first = await Sold(await Sell(till, request));
        var again = await Sold(await Sell(till, request));

        Assert.Equal(first.GetProperty("orderId").GetInt32(), again.GetProperty("orderId").GetInt32());
        Assert.False(first.GetProperty("replayed").GetBoolean());
        Assert.True(again.GetProperty("replayed").GetBoolean());
        Assert.Equal(before - 3, await StockOf(till, 11));
    }

    [Fact]
    public async Task Two_checkouts_racing_with_the_same_key_make_one_sale()
    {
        var (till, _) = await Till();
        var before = await StockOf(till, 12);
        var cart = Cart(Usd, [Line(12, quantity: 2)]);
        var total = (await Price(till, cart)).GetProperty("total").GetDecimal();
        var request = Checkout(cart, total, Cash, key: "double-click-double-click");

        var both = await Task.WhenAll(Sell(till, request), Sell(till, request));

        var sales = await Task.WhenAll(both.Select(Sold));
        Assert.Single(sales.Select(s => s.GetProperty("orderId").GetInt32()).Distinct());
        Assert.Single(sales, s => !s.GetProperty("replayed").GetBoolean());
        Assert.Equal(before - 2, await StockOf(till, 12));
    }

    [Fact]
    public async Task The_same_key_for_a_different_cart_is_refused_instead_of_returning_someone_elses_sale()
    {
        var (till, _) = await Till();
        await Sold(await Sell(till, Checkout(Cart(Usd, [Line(14)]), (await Price(till, Cart(Usd, [Line(14)]))).GetProperty("total").GetDecimal(), Cash, key: "reused-key-reused-key")));

        var other = await Sell(till, Checkout(Cart(Usd, [Line(15, quantity: 2)]), 1m, Cash, key: "reused-key-reused-key"));

        await AssertProblem(other, HttpStatusCode.Conflict, "idempotency_key_reused");
    }

    // ---- stock ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Selling_more_than_is_in_stock_is_refused_until_the_cashier_confirms_and_then_stock_floors_at_zero()
    {
        var (till, _) = await Till();
        var onHand = await StockOf(till, 5); // Apple Juice, 8
        var cart = Cart(Usd, [Line(5, quantity: (int)onHand + 2)]);
        var total = (await Price(till, cart)).GetProperty("total").GetDecimal();
        const string key = "sell-anyway-sell-anyway";

        var cartsBefore = (await Body(await till.GetAsync("/api/v1/carts"))).GetArrayLength();
        var refused = await Sell(till, Checkout(cart, total, Cash, key: key));
        var cartsAfter = (await Body(await till.GetAsync("/api/v1/carts"))).GetArrayLength();
        Assert.Equal(onHand, await StockOf(till, 5)); // nothing was taken
        var confirmed = await Sold(await Sell(till, Checkout(cart, total, Cash, key: key, allowInsufficientStock: true))); // same key: a refused attempt does not burn it

        await AssertProblem(refused, HttpStatusCode.Conflict, "insufficient_stock");
        Assert.Equal(cartsBefore, cartsAfter); // and the half-built cart did not turn up as a parked one
        Assert.False(confirmed.GetProperty("replayed").GetBoolean());
        Assert.Equal(0m, await StockOf(till, 5));
    }

    // ---- who pays ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_sale_on_account_adds_to_the_customers_debt()
    {
        var (till, _) = await Till();
        var before = await BalanceOf(till, "Elias Sfeir");
        var cart = Cart(Usd, [Line(21, quantity: 2)]);
        var total = (await Price(till, cart)).GetProperty("total").GetDecimal();

        var sale = await Sold(await Sell(till, Checkout(cart, total, paymentMethodId: 0, customerId: 9, addToDebt: true)));

        Assert.True(sale.GetProperty("addedToDebt").GetBoolean());
        Assert.Equal(9, sale.GetProperty("customerId").GetInt32());
        Assert.Equal("Elias Sfeir", sale.GetProperty("customerName").GetString());
        Assert.Equal(before + total, await BalanceOf(till, "Elias Sfeir"));
    }

    [Fact]
    public async Task A_walk_in_cannot_buy_on_account_and_an_unknown_customer_or_payment_method_is_refused()
    {
        var (till, _) = await Till();
        var cart = Cart(Usd, [Line(22)]);

        await AssertProblem(await Sell(till, Checkout(cart, 1m, addToDebt: true)), HttpStatusCode.BadRequest, "debt_requires_customer");
        await AssertProblem(await Sell(till, Checkout(cart, 1m, Cash, customerId: 9999)), HttpStatusCode.Conflict, "customer_not_found");
        await AssertProblem(await Sell(till, Checkout(cart, 1m, paymentMethodId: 999)), HttpStatusCode.BadRequest, "payment_method_invalid");
    }

    [Fact]
    public async Task Card_payments_work_like_cash()
    {
        var (till, _) = await Till();
        var cart = Cart(Usd, [Line(25)]);
        var total = (await Price(till, cart)).GetProperty("total").GetDecimal();

        var sale = await Sold(await Sell(till, Checkout(cart, total, Card)));

        Assert.Equal("Card", sale.GetProperty("paymentMethod").GetString());
    }

    // ---- currencies ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Eur)]
    [InlineData(Lbp)]
    public async Task A_cart_in_another_currency_is_sold_at_the_converted_prices_the_cashier_saw(int currencyId)
    {
        var (till, _) = await Till();
        var cart = Cart(currencyId, [Line(26, quantity: 3)]);
        var priced = await Price(till, cart);
        var total = priced.GetProperty("total").GetDecimal();

        var sale = await Sold(await Sell(till, Checkout(cart, total, Cash)));

        Assert.Equal(currencyId, sale.GetProperty("currencyId").GetInt32());
        Assert.Equal(total, sale.GetProperty("total").GetDecimal());
        Assert.Equal(priced.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal(), sale.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
        Assert.True(total > 3m, "the amount is in the foreign currency, not the product's USD price");
    }

    // ---- drawer ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task With_require_open_drawer_a_closed_drawer_refuses_the_sale_and_an_open_one_links_it()
    {
        var (till, userId) = await Till();
        var cart = Cart(Usd, [Line(27)]);
        var total = (await Price(till, cart)).GetProperty("total").GetDecimal();
        var before = await StockOf(till, 27);

        var notOpen = await Sell(till, Checkout(cart, total, Cash, requireOpenDrawer: true));
        var drawerBefore = await Body(await till.GetAsync("/api/v1/pos/drawer"));
        var allowedAnyway = await Sold(await Sell(till, Checkout(Cart(Usd, [Line(28)]), (await Price(till, Cart(Usd, [Line(28)]))).GetProperty("total").GetDecimal(), Cash)));

        int sessionId;
        using (var scope = Selling(_factory).Services.CreateScope())
            sessionId = (await scope.ServiceProvider.GetRequiredService<ICashDrawerService>().OpenDrawerAsync(userId, 100m)).Id;
        var drawerOpen = await Body(await till.GetAsync("/api/v1/pos/drawer"));
        var linked = await Sold(await Sell(till, Checkout(cart, total, Cash, requireOpenDrawer: true)));

        await AssertProblem(notOpen, HttpStatusCode.Conflict, "drawer_not_open");
        Assert.False(drawerBefore.GetProperty("isOpen").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, allowedAnyway.GetProperty("cashDrawerSessionId").ValueKind); // the old till sold anyway, and still does by default
        Assert.True(drawerOpen.GetProperty("isOpen").GetBoolean());
        Assert.Equal(sessionId, drawerOpen.GetProperty("sessionId").GetInt32());
        Assert.Equal(100m, drawerOpen.GetProperty("openingCash").GetDecimal());
        Assert.Equal(sessionId, linked.GetProperty("cashDrawerSessionId").GetInt32());
        Assert.Equal(before - 1, await StockOf(till, 27));
    }

    // ---- licence --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task While_the_licence_stops_selling_checkout_and_holding_are_403_but_reading_still_works()
    {
        var (till, _) = await SignedIn(_factory, ApiFactory.CashierUser, ApiFactory.CashierPin);

        var checkout = await Sell(till, Checkout(Cart(Usd, [Line(48)]), 3.2m, Cash));
        var hold = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/carts", new { cart = Cart(Usd, [Line(48)]) }));
        var read = await till.GetAsync("/api/v1/pos/products?pageSize=1");

        await AssertProblem(checkout, HttpStatusCode.Forbidden, "license_sales_stopped");
        await AssertProblem(hold, HttpStatusCode.Forbidden, "license_sales_stopped");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    // ---- after the sale -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_last_sale_can_be_found_reprinted_and_downloaded_as_a_pdf()
    {
        var (till, _) = await Till();
        var cart = Cart(Usd, [Line(29)]);
        var sale = await Sold(await Sell(till, Checkout(cart, (await Price(till, cart)).GetProperty("total").GetDecimal(), Cash)));
        var orderId = sale.GetProperty("orderId").GetInt32();

        var recent = await Body(await till.GetAsync("/api/v1/pos/sales/recent?count=20")); // the demo seed dates some receipts later today, so do not assume ours is first
        var mine = recent.EnumerateArray().Single(r => r.GetProperty("orderId").GetInt32() == orderId);
        var print = await till.SendAsync(Json(HttpMethod.Post, $"/api/v1/pos/sales/{orderId}/print"));
        var pdf = await till.GetAsync($"/api/v1/pos/sales/{orderId}/receipt.pdf");
        var unknown = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/sales/99999/print"));

        Assert.False(string.IsNullOrEmpty(mine.GetProperty("receiptNumber").GetString()));
        Assert.Equal(sale.GetProperty("orderNumber").GetString(), mine.GetProperty("orderNumber").GetString());
        Assert.Equal(HttpStatusCode.OK, print.StatusCode); // the test host runs with NEXPOS_DEMO=1: a no-op printer that reports success, never hardware
        var printed = await Body(print);
        Assert.True(printed.GetProperty("printed").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(printed.GetProperty("message").GetString()));
        Assert.EndsWith($"/{orderId}/receipt.pdf", printed.GetProperty("pdfUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync()).Take(4).ToArray()));
        await AssertProblem(unknown, HttpStatusCode.NotFound, "order_not_found");
    }

    // ---- held carts -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_cart_can_be_parked_listed_resumed_and_deleted()
    {
        var (till, _) = await Till();
        var stockBefore = await StockOf(till, 30);

        var hold = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/carts", new
        {
            cart = Cart(Usd, [Line(30, quantity: 2), Line(17, weightOrVolume: 0.25m)], discountPercentage: 5m),
            notes = "back in 5 minutes",
        }));
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var held = await Body(hold);
        var id = held.GetProperty("id").GetInt32();

        var list = (await Body(await till.GetAsync("/api/v1/carts"))).EnumerateArray().ToList();
        var resumed = await Body(await till.GetAsync($"/api/v1/carts/{id}"));

        Assert.True(held.GetProperty("isWalkIn").GetBoolean());
        Assert.Equal("back in 5 minutes", held.GetProperty("notes").GetString());
        Assert.Equal(3, held.GetProperty("itemCount").GetInt32());
        Assert.Contains(list, c => c.GetProperty("id").GetInt32() == id);
        Assert.Equal(id, resumed.GetProperty("cartId").GetInt32());
        Assert.Equal(2, resumed.GetProperty("lines").GetArrayLength());
        Assert.Equal("default", resumed.GetProperty("lines")[0].GetProperty("priceKind").GetString());
        Assert.Equal(0.25m, resumed.GetProperty("lines")[1].GetProperty("weightOrVolume").GetDecimal());
        Assert.Equal(5m, resumed.GetProperty("discountPercentage").GetDecimal());
        Assert.Equal("back in 5 minutes", resumed.GetProperty("notes").GetString());
        Assert.Equal(stockBefore, await StockOf(till, 30)); // parking reserves nothing

        Assert.Equal(HttpStatusCode.NoContent, (await till.SendAsync(Json(HttpMethod.Delete, $"/api/v1/carts/{id}"))).StatusCode);
        await AssertProblem(await till.GetAsync($"/api/v1/carts/{id}"), HttpStatusCode.NotFound, "cart_not_found");
        await AssertProblem(await till.SendAsync(Json(HttpMethod.Delete, $"/api/v1/carts/{id}")), HttpStatusCode.NotFound, "cart_not_found");
    }

    [Fact]
    public async Task A_parked_cart_for_a_customer_remembers_the_customer_and_the_chosen_price_kind()
    {
        var (till, _) = await Till();
        var min = (await Body(await till.GetAsync("/api/v1/pos/products/31/prices?currencyId=1"))).GetProperty("minPrice").GetDecimal();

        var held = await Body(await till.SendAsync(Json(HttpMethod.Post, "/api/v1/carts", new
        {
            cart = Cart(Usd, [Line(31, customPrice: min + 1m)]),
            customerId = 6,
        })));
        var resumed = await Body(await till.GetAsync($"/api/v1/carts/{held.GetProperty("id").GetInt32()}"));

        Assert.False(held.GetProperty("isWalkIn").GetBoolean());
        Assert.Equal("Hala Fakhoury", held.GetProperty("customerName").GetString());
        Assert.Equal(6, resumed.GetProperty("customerId").GetInt32());
        Assert.Equal("custom", resumed.GetProperty("lines")[0].GetProperty("priceKind").GetString());
        Assert.Equal(min + 1m, resumed.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
    }

    [Fact]
    public async Task Parking_applies_the_checks_of_a_sale_and_delete_all_clears_the_parked_carts()
    {
        var (till, _) = await Till();

        var duplicate = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/carts", new { cart = Cart(Usd, [Line(33), Line(33)]) }));
        await till.SendAsync(Json(HttpMethod.Post, "/api/v1/carts", new { cart = Cart(Usd, [Line(33)]) }));
        await till.SendAsync(Json(HttpMethod.Post, "/api/v1/carts", new { cart = Cart(Usd, [Line(34)]) }));
        var deleted = await Body(await till.SendAsync(Json(HttpMethod.Delete, "/api/v1/carts")));
        var after = await Body(await till.GetAsync("/api/v1/carts"));

        await AssertProblem(duplicate, HttpStatusCode.BadRequest, "duplicate_product");
        Assert.True(deleted.GetProperty("deletedCount").GetInt32() >= 2);
        Assert.Equal(0, after.GetArrayLength());
    }

    [Fact]
    public async Task Delete_all_leaves_the_carts_of_old_sales_alone_however_old_they_are()
    {
        var (till, _) = await Till();
        using var scope = Selling(_factory).Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        // The seeded demo sales are all older than the old 30-day window the first version of "held" used.
        var old = (await orders.GetByDateRangeAsync(DateTime.UtcNow.AddYears(-5), DateTime.UtcNow.AddDays(-31))).First(o => o.Cart.CartItems.Count > 0);
        var items = old.Cart.CartItems.Count;

        await till.SendAsync(Json(HttpMethod.Post, "/api/v1/carts", new { cart = Cart(Usd, [Line(33)]) }));
        var deleted = await Body(await till.SendAsync(Json(HttpMethod.Delete, "/api/v1/carts")));

        var again = await Selling(_factory).Services.CreateScope().ServiceProvider.GetRequiredService<IOrderService>().GetByIdAsync(old.Id);
        Assert.True(deleted.GetProperty("deletedCount").GetInt32() >= 1);
        Assert.False(again.Cart.IsDeleted);
        Assert.Equal(items, again.Cart.CartItems.Count);
    }

    [Fact]
    public async Task A_sold_cart_never_shows_up_as_a_parked_one()
    {
        var (till, _) = await Till();
        var cart = Cart(Usd, [Line(35)]);
        var before = (await Body(await till.GetAsync("/api/v1/carts"))).GetArrayLength();
        await Sold(await Sell(till, Checkout(cart, (await Price(till, cart)).GetProperty("total").GetDecimal(), Cash)));

        var list = await Body(await till.GetAsync("/api/v1/carts"));

        Assert.Equal(before, list.GetArrayLength());
    }
}
