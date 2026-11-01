using System.Net;
using System.Net.Http.Json;
using Application.IServices.Products;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Infrastructure;
using Xunit;
using static Tests.Api.Pos.PosTestKit;

namespace Tests.Api.Pos;

/// <summary>
/// The reading side of the till through the real pipeline: who may call it, what the catalog and search return, and the server's price of a cart
/// (units, tiers, custom prices, currencies, discounts, stock warnings). Demo data: USD is the default currency, EUR 1.1, LBP 89 500;
/// product 48 "AA Batteries" 3.20 USD (cost below), 17 "Akkawi Cheese" 11.50 USD per kg, 9 "Arabic Bread" 0.90 USD.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PosCatalogAndPricingTests
{
    private const int Batteries = 48;
    private const int Cheese = 17;
    private const int Bread = 9;
    private const int Usd = 1, Eur = 2, Lbp = 3;

    private readonly ApiFactory _factory;

    public PosCatalogAndPricingTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> Till() => (await Cashier(Selling(_factory))).Browser;

    // ---- access ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/v1/pos/products")]
    [InlineData("/api/v1/pos/categories")]
    [InlineData("/api/v1/pos/currencies")]
    [InlineData("/api/v1/pos/payment-methods")]
    [InlineData("/api/v1/pos/customers")]
    [InlineData("/api/v1/pos/drawer")]
    [InlineData("/api/v1/carts")]
    [InlineData("/api/v1/pos/sales/recent")]
    public async Task Without_a_cookie_every_till_endpoint_answers_401_json(string url)
    {
        var response = await Selling(_factory).CreateClient().GetAsync(url);

        await AssertProblem(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_mutating_call_without_the_client_header_is_refused()
    {
        var till = await Till();

        var response = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/price", Cart(Usd, [Line(Batteries)]), clientHeader: false));

        await AssertProblem(response, HttpStatusCode.Forbidden, "client_header_required");
    }

    // ---- catalog ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Products_come_in_pages_with_everything_a_tile_needs()
    {
        var till = await Till();

        var page = await Body(await till.GetAsync("/api/v1/pos/products?page=1&pageSize=5"));

        // The shared test database grows as other API tests add products: only check that the page is a page of a bigger list.
        Assert.True(page.GetProperty("total").GetInt32() >= 49);
        Assert.Equal(5, page.GetProperty("items").GetArrayLength());
        var first = Assert.Single((await Body(await till.GetAsync("/api/v1/pos/products?q=AA%20Batteries"))).GetProperty("items").EnumerateArray());
        Assert.Equal("AA Batteries 4-Pack", first.GetProperty("name").GetString());
        Assert.Equal("Electronics", first.GetProperty("categoryName").GetString());
        Assert.Equal("$", first.GetProperty("currencyNotation").GetString());
        Assert.Equal("count", first.GetProperty("unitKind").GetString());
        Assert.False(page.GetProperty("isMainScreen").GetBoolean());
        Assert.False(first.TryGetProperty("costPrice", out _)); // the purchase price never goes to the till in a product tile
    }

    [Fact]
    public async Task Products_filter_by_category_and_free_text()
    {
        var till = await Till();

        var beverages = await Body(await till.GetAsync("/api/v1/pos/products?categoryId=3&pageSize=50"));
        var juice = await Body(await till.GetAsync("/api/v1/pos/products?q=apple%20juice"));

        Assert.Equal(8, beverages.GetProperty("total").GetInt32());
        Assert.All(beverages.GetProperty("items").EnumerateArray(), p => Assert.Equal(3, p.GetProperty("categoryId").GetInt32()));
        Assert.Equal("Apple Juice 1L", Assert.Single(juice.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Weight_products_say_so_and_the_main_screen_falls_back_to_all_products_without_a_display_order()
    {
        var till = await Till();

        var cheese = await Body(await till.GetAsync($"/api/v1/pos/products/{Cheese}"));
        var main = await Body(await till.GetAsync("/api/v1/pos/products?mainScreen=true&pageSize=3"));
        var all = await Body(await till.GetAsync("/api/v1/pos/products?pageSize=3"));

        Assert.Equal("weight", cheese.GetProperty("unitKind").GetString());
        Assert.Equal("kg", cheese.GetProperty("unit").GetString());
        Assert.Equal(all.GetProperty("total").GetInt32(), main.GetProperty("total").GetInt32());
        Assert.False(main.GetProperty("isMainScreen").GetBoolean());
    }

    [Fact]
    public async Task Sorting_by_anything_but_name_is_a_validation_error()
    {
        var till = await Till();

        var response = await till.GetAsync("/api/v1/pos/products?sort=costPrice");

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Categories_list_only_those_with_something_to_sell()
    {
        var till = await Till();

        var categories = (await Body(await till.GetAsync("/api/v1/pos/categories"))).EnumerateArray().ToList();
        var all = await Body(await till.GetAsync("/api/v1/pos/products?pageSize=1"));

        Assert.True(categories.Count >= 10);
        Assert.All(categories, c => Assert.True(c.GetProperty("productCount").GetInt32() > 0));
        Assert.Equal(all.GetProperty("total").GetInt32(), categories.Sum(c => c.GetProperty("productCount").GetInt32()));
    }

    // ---- search ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_exact_barcode_is_a_single_barcode_hit()
    {
        var till = await Till();

        var result = await Body(await till.GetAsync("/api/v1/pos/products/search?q=6001000000487"));

        Assert.Equal("barcode", result.GetProperty("searchType").GetString());
        Assert.Equal(Batteries, Assert.Single(result.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
        Assert.False(result.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task A_name_search_returns_matches_and_a_scanner_miss_is_a_404_with_a_code()
    {
        var till = await Till();

        var byName = await Body(await till.GetAsync("/api/v1/pos/products/search?q=cheese"));
        var miss = await till.GetAsync("/api/v1/pos/products/search?q=0000000000000&barcodeOnly=true");
        var emptyName = await Body(await till.GetAsync("/api/v1/pos/products/search?q=zzzzzz"));

        Assert.Equal("name", byName.GetProperty("searchType").GetString());
        Assert.NotEmpty(byName.GetProperty("items").EnumerateArray());
        await AssertProblem(miss, HttpStatusCode.NotFound, "product_not_found");
        Assert.Empty(emptyName.GetProperty("items").EnumerateArray()); // a name search with no hit is not an error
    }

    [Fact]
    public async Task Name_search_pages_with_skip_and_has_more()
    {
        var till = await Till();

        var first = await Body(await till.GetAsync("/api/v1/pos/products/search?q=a"));
        var second = await Body(await till.GetAsync("/api/v1/pos/products/search?q=a&skip=20"));

        Assert.Equal(20, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.NotEqual(
            first.GetProperty("items")[0].GetProperty("id").GetInt32(),
            second.GetProperty("items")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task An_unknown_product_is_a_404()
    {
        var till = await Till();

        await AssertProblem(await till.GetAsync("/api/v1/pos/products/99999"), HttpStatusCode.NotFound, "product_not_found");
    }

    // ---- reference lists ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reference_lists_hide_the_system_debt_payment_method_and_put_the_default_currency_first()
    {
        var till = await Till();

        var methods = (await Body(await till.GetAsync("/api/v1/pos/payment-methods"))).EnumerateArray().Select(m => m.GetProperty("name").GetString()).ToList();
        var currencies = (await Body(await till.GetAsync("/api/v1/pos/currencies"))).EnumerateArray().ToList();

        Assert.DoesNotContain("Customer Debt", methods);
        Assert.Contains("Cash", methods);
        Assert.Equal("USD", currencies[0].GetProperty("code").GetString());
        Assert.True(currencies[0].GetProperty("isDefault").GetBoolean());
        Assert.Equal(1.1m, currencies.Single(c => c.GetProperty("code").GetString() == "EUR").GetProperty("rateToUsd").GetDecimal());
    }

    [Fact]
    public async Task Customers_are_lazy_loaded_a_page_at_a_time()
    {
        var till = await Till();

        var page1 = await Body(await till.GetAsync("/api/v1/pos/customers?pageSize=3"));
        var page2 = await Body(await till.GetAsync("/api/v1/pos/customers?pageSize=3&page=2"));
        var found = await Body(await till.GetAsync("/api/v1/pos/customers?q=Hala"));

        Assert.Equal(3, page1.GetProperty("items").GetArrayLength());
        Assert.True(page1.GetProperty("hasMore").GetBoolean());
        Assert.Equal(2, page2.GetProperty("page").GetInt32());
        Assert.Equal("Hala Fakhoury", Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(45m, found.GetProperty("items")[0].GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task A_customer_can_be_added_from_the_till_and_a_too_short_name_is_a_field_error()
    {
        var till = await Till();

        var created = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/customers", new { firstName = "Nadim", lastName = "Haddad" }));
        var tooShort = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/customers", new { firstName = "N ", lastName = "Haddad" }));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Nadim Haddad", (await Body(created)).GetProperty("name").GetString());
        await AssertProblem(tooShort, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(tooShort)).GetProperty("errors").TryGetProperty("firstName", out _));
    }

    // ---- price options ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Price_options_are_converted_into_the_requested_currency()
    {
        var till = await Till();

        var usd = await Body(await till.GetAsync($"/api/v1/pos/products/{Batteries}/prices?currencyId={Usd}"));
        var lbp = await Body(await till.GetAsync($"/api/v1/pos/products/{Batteries}/prices?currencyId={Lbp}"));

        var usdDefault = usd.GetProperty("options")[0];
        Assert.True(JsonValueKindNull(usdDefault, "id"));
        Assert.Equal(3.2m, usdDefault.GetProperty("price").GetDecimal());
        Assert.Equal(3.2m * 89500m, lbp.GetProperty("options")[0].GetProperty("price").GetDecimal());
        Assert.Equal(usd.GetProperty("minPrice").GetDecimal() * 89500m, lbp.GetProperty("minPrice").GetDecimal(), 0);
    }

    [Fact]
    public async Task Price_options_for_an_unknown_currency_are_a_field_error_and_for_an_unknown_product_a_404()
    {
        var till = await Till();

        await AssertProblem(await till.GetAsync($"/api/v1/pos/products/{Batteries}/prices?currencyId=99"), HttpStatusCode.BadRequest, "currency_not_found");
        await AssertProblem(await till.GetAsync($"/api/v1/pos/products/99999/prices?currencyId={Usd}"), HttpStatusCode.NotFound, "product_not_found");
    }

    private static bool JsonValueKindNull(System.Text.Json.JsonElement e, string name)
        => e.GetProperty(name).ValueKind == System.Text.Json.JsonValueKind.Null;

    // ---- price -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_cart_is_priced_by_the_server_with_pieces_and_weight_lines()
    {
        var till = await Till();

        var priced = await Price(till, Cart(Usd, [Line(Batteries, quantity: 3), Line(Cheese, weightOrVolume: 0.75m)]));

        Assert.Equal(9.6m, priced.GetProperty("lines")[0].GetProperty("lineTotal").GetDecimal());
        Assert.Equal(8.625m, priced.GetProperty("lines")[1].GetProperty("lineTotal").GetDecimal());
        Assert.Equal(18.225m, priced.GetProperty("subtotal").GetDecimal());
        Assert.Equal(18.22m, priced.GetProperty("total").GetDecimal()); // 18.225 -> the even neighbour
        Assert.Equal(0m, priced.GetProperty("taxAmount").GetDecimal());
        Assert.Equal("$", priced.GetProperty("currencyNotation").GetString());
    }

    [Fact]
    public async Task A_percentage_discount_is_shown_both_ways()
    {
        var till = await Till();

        var priced = await Price(till, Cart(Usd, [Line(Batteries, quantity: 3), Line(Cheese, weightOrVolume: 0.75m)], discountPercentage: 10m, discountLabel: "Staff"));

        Assert.Equal(1.8225m, priced.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(10m, priced.GetProperty("discountPercentage").GetDecimal());
        Assert.Equal(16.40m, priced.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_flat_discount_reports_its_percentage_and_a_percentage_wins_when_both_are_sent()
    {
        var till = await Till();

        var flat = await Price(till, Cart(Usd, [Line(Batteries, quantity: 5)], discountAmount: 4m));
        var both = await Price(till, Cart(Usd, [Line(Batteries, quantity: 5)], discountAmount: 4m, discountPercentage: 50m));

        Assert.Equal(25m, flat.GetProperty("discountPercentage").GetDecimal());
        Assert.Equal(12m, flat.GetProperty("total").GetDecimal());
        Assert.Equal(8m, both.GetProperty("discountAmount").GetDecimal());
    }

    [Theory]
    [InlineData(Eur, 3.52)]
    [InlineData(Lbp, 286400)]
    public async Task A_cart_in_another_currency_converts_each_unit_price_and_rounds_it_to_cents(int currencyId, double expectedUnit)
    {
        var till = await Till();

        var priced = await Price(till, Cart(currencyId, [Line(Batteries, quantity: 2)]));

        var line = priced.GetProperty("lines")[0];
        Assert.Equal((decimal)expectedUnit, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal((decimal)expectedUnit * 2, line.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(currencyId, priced.GetProperty("currencyId").GetInt32());
    }

    [Fact]
    public async Task A_stored_price_tier_prices_the_line_and_a_tier_of_another_product_is_refused()
    {
        var till = await Till();
        int wholesale;
        using (var scope = Selling(_factory).Services.CreateScope())
        {
            var prices = scope.ServiceProvider.GetRequiredService<IProductPriceService>();
            await prices.ReplaceTiersAsync(Bread, [new Application.DTOs.Products.ProductPriceInput { Name = "Wholesale", Price = 0.7m }]);
            wholesale = (await prices.GetTiersAsync(Bread)).Single().Id;
        }

        var priced = await Price(till, Cart(Usd, [Line(Bread, quantity: 10, priceTierId: wholesale)]));
        var elsewhere = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/price", Cart(Usd, [Line(Batteries, priceTierId: wholesale)])));

        var line = priced.GetProperty("lines")[0];
        Assert.Equal(0.7m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Wholesale", line.GetProperty("priceLabel").GetString());
        Assert.Equal(7m, priced.GetProperty("total").GetDecimal());
        await AssertProblem(elsewhere, HttpStatusCode.BadRequest, "price_not_available");
    }

    [Fact]
    public async Task A_custom_price_is_accepted_at_or_above_the_purchase_price_and_refused_below_it()
    {
        var till = await Till();
        var min = (await Body(await till.GetAsync($"/api/v1/pos/products/{Batteries}/prices?currencyId={Usd}"))).GetProperty("minPrice").GetDecimal();
        Assert.True(min > 0, "the demo product has a purchase price");

        var atMinimum = await Price(till, Cart(Usd, [Line(Batteries, customPrice: min)]));
        var below = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/price", Cart(Usd, [Line(Batteries, customPrice: min - 0.01m)])));

        var line = atMinimum.GetProperty("lines")[0];
        Assert.True(line.GetProperty("isCustomPrice").GetBoolean());
        Assert.Equal(min, line.GetProperty("unitPrice").GetDecimal());
        await AssertProblem(below, HttpStatusCode.BadRequest, "price_below_minimum");
    }

    [Fact]
    public async Task A_line_that_would_take_stock_below_zero_is_flagged()
    {
        var till = await Till();

        var priced = await Price(till, Cart(Usd, [Line(5, quantity: 9), Line(Batteries)])); // product 5 has 8 on hand

        Assert.True(priced.GetProperty("lines")[0].GetProperty("insufficientStock").GetBoolean());
        Assert.False(priced.GetProperty("lines")[1].GetProperty("insufficientStock").GetBoolean());
        Assert.True(priced.GetProperty("hasInsufficientStock").GetBoolean());
    }

    [Fact]
    public async Task Bad_carts_get_stable_codes()
    {
        var till = await Till();
        async Task<HttpResponseMessage> Post(object cart) => await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/price", cart));

        await AssertProblem(await Post(Cart(Usd, [Line(99999)])), HttpStatusCode.NotFound, "product_not_found");
        await AssertProblem(await Post(Cart(Usd, [Line(Batteries), Line(Batteries, 2)])), HttpStatusCode.BadRequest, "duplicate_product");
        await AssertProblem(await Post(Cart(99, [Line(Batteries)])), HttpStatusCode.BadRequest, "currency_not_found");
        await AssertProblem(await Post(Cart(Usd, [Line(Batteries)], discountAmount: 50m)), HttpStatusCode.BadRequest, "invalid_discount");
        await AssertProblem(await Post(Cart(Usd, [Line(Batteries)], discountPercentage: 101m)), HttpStatusCode.BadRequest, "invalid_discount");
        await AssertProblem(await Post(Cart(Usd, [Line(Cheese, weightOrVolume: 0m)])), HttpStatusCode.BadRequest, "invalid_amount");
        await AssertProblem(await Post(Cart(Usd, [])), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Messages_come_back_in_the_language_of_the_request()
    {
        var till = await Till();

        var response = await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/price", Cart(Usd, [Line(Batteries), Line(Batteries, 2)]), language: "ar"));

        var detail = (await Body(response)).GetProperty("detail").GetString();
        Assert.Contains("أكثر من سطر", detail);
    }
}
