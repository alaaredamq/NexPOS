using System.Net;
using Tests.Api.Infrastructure;
using Web.Api.Catalog;
using Xunit;

namespace Tests.Api.Catalog;

/// <summary>Product display order API (track C2): who may call it, add / remove / reorder, the picker and the quick-add (which creates a product and its stock).</summary>
[Collection(ApiCollection.Name)]
public sealed class DisplayOrderApiTests : CatalogApiTestBase
{
    private const string Url = "/api/v1/product-display-order";

    public DisplayOrderApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static async Task<(IReadOnlyList<int> Ids, IReadOnlyList<string> Names)> CurrentAsync(HttpClient admin)
    {
        var body = await Body(await admin.GetAsync(Url));
        var items = body.GetProperty("items").EnumerateArray().ToList();
        return (items.Select(i => i.GetProperty("id").GetInt32()).ToList(), items.Select(i => i.GetProperty("name").GetString()!).ToList());
    }

    private static async Task<(int ProductId, string Name)> FirstAvailableAsync(HttpClient admin, int index = 0)
    {
        var body = await Body(await admin.GetAsync($"{Url}/available-products?take=10"));
        var product = body.GetProperty("items")[index];
        return (product.GetProperty("id").GetInt32(), product.GetProperty("name").GetString()!);
    }

    private static async Task<int> AddAsync(HttpClient admin, int productId)
    {
        var response = await admin.SendAsync(Json(HttpMethod.Post, $"{Url}/items", new { productId }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("id").GetInt32();
    }

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Everything_needs_MainScreenProducts_Manage_a_cashier_gets_403()
    {
        var cashier = await Cashier();

        await AssertProblem(await cashier.GetAsync(Url), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.GetAsync($"{Url}/available-products"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Put, $"{Url}/order", new { ids = new[] { 1 } })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"{Url}/items", new { productId = 1 })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Delete, $"{Url}/items/1")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"{Url}/quick-add", new { name = "X", price = 1 })), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Without_a_cookie_the_list_answers_401()
        => await AssertProblem(await Factory.CreateBrowser().GetAsync(Url), HttpStatusCode.Unauthorized, "unauthenticated");

    // ---- add / remove ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_product_added_goes_to_the_end_leaves_the_picker_and_can_be_removed()
    {
        var admin = await Admin();
        var (productId, name) = await FirstAvailableAsync(admin);

        var addResponse = await admin.SendAsync(Json(HttpMethod.Post, $"{Url}/items", new { productId }));
        var added = await Body(addResponse);
        var id = added.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);
        Assert.Equal(name, added.GetProperty("name").GetString());
        var list = await Body(await admin.GetAsync(Url));
        var last = list.GetProperty("items").EnumerateArray().Last();
        Assert.Equal(id, last.GetProperty("id").GetInt32());
        Assert.Equal(list.GetProperty("items").GetArrayLength(), last.GetProperty("order").GetInt32());

        var picker = await Body(await admin.GetAsync($"{Url}/available-products?q={Uri.EscapeDataString(name)}&take=50"));
        Assert.DoesNotContain(picker.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetInt32() == productId);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"{Url}/items/{id}"))).StatusCode);
        Assert.DoesNotContain((await CurrentAsync(admin)).Ids, x => x == id);
        var again = await Body(await admin.GetAsync($"{Url}/available-products?q={Uri.EscapeDataString(name)}&take=50"));
        Assert.Contains(again.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetInt32() == productId);
    }

    [Fact]
    public async Task Adding_the_same_product_twice_is_a_409_and_an_unknown_product_a_404()
    {
        var admin = await Admin();
        var (productId, _) = await FirstAvailableAsync(admin);
        var id = await AddAsync(admin, productId);

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, $"{Url}/items", new { productId })), HttpStatusCode.Conflict, DisplayOrderException.AlreadyInDisplayOrder);
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, $"{Url}/items", new { productId = 999999 })), HttpStatusCode.NotFound, DisplayOrderException.ProductNotFound);

        await admin.SendAsync(Json(HttpMethod.Delete, $"{Url}/items/{id}"));
    }

    [Fact]
    public async Task Removing_an_unknown_row_is_404()
    {
        var admin = await Admin();

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"{Url}/items/999999")), HttpStatusCode.NotFound, "not_found");
    }

    // ---- reorder -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reorder_saves_the_listed_order_and_numbers_the_rows_from_one()
    {
        var admin = await Admin();
        var a = await AddAsync(admin, (await FirstAvailableAsync(admin)).ProductId);
        var b = await AddAsync(admin, (await FirstAvailableAsync(admin)).ProductId);
        var c = await AddAsync(admin, (await FirstAvailableAsync(admin)).ProductId);
        var others = (await CurrentAsync(admin)).Ids.Where(i => i != a && i != b && i != c).ToList();

        var response = await admin.SendAsync(Json(HttpMethod.Put, $"{Url}/order", new { ids = new[] { c, a, b }.Concat(others) }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await Body(await admin.GetAsync(Url));
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { c, a, b }.Concat(others), items.Select(i => i.GetProperty("id").GetInt32()));
        Assert.Equal(Enumerable.Range(1, items.Count), items.Select(i => i.GetProperty("order").GetInt32()));

        foreach (var id in new[] { a, b, c }) await admin.SendAsync(Json(HttpMethod.Delete, $"{Url}/items/{id}"));
    }

    [Fact]
    public async Task A_stale_reorder_list_keeps_unlisted_rows_after_the_listed_ones_with_no_duplicate_positions()
    {
        var admin = await Admin();
        var a = await AddAsync(admin, (await FirstAvailableAsync(admin)).ProductId);
        var b = await AddAsync(admin, (await FirstAvailableAsync(admin)).ProductId);

        // The client only knew about b (a was added by someone else meanwhile); 999999 is a row that was removed meanwhile.
        await admin.SendAsync(Json(HttpMethod.Put, $"{Url}/order", new { ids = new[] { b, 999999 } }));

        var items = (await Body(await admin.GetAsync(Url))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(b, items[0].GetProperty("id").GetInt32());
        Assert.Contains(items, i => i.GetProperty("id").GetInt32() == a);
        Assert.Equal(Enumerable.Range(1, items.Count), items.Select(i => i.GetProperty("order").GetInt32()));

        foreach (var id in new[] { a, b }) await admin.SendAsync(Json(HttpMethod.Delete, $"{Url}/items/{id}"));
    }

    [Fact]
    public async Task Reorder_with_an_empty_list_is_a_validation_error()
    {
        var admin = await Admin();

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Put, $"{Url}/order", new { ids = Array.Empty<int>() })), HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---- picker ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_picker_pages_with_has_more_and_caps_take()
    {
        var admin = await Admin();

        var first = await Body(await admin.GetAsync($"{Url}/available-products?take=2"));
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("hasMore").GetBoolean());

        var second = await Body(await admin.GetAsync($"{Url}/available-products?take=2&skip=2"));
        Assert.Empty(second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32())
            .Intersect(first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32())));

        var huge = await Body(await admin.GetAsync($"{Url}/available-products?take=100000"));
        Assert.True(huge.GetProperty("items").GetArrayLength() <= DisplayOrderApiService.MaxTake);
    }

    // ---- quick add ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Quick_add_creates_the_product_with_its_stock_a_valid_ean13_and_puts_it_on_the_main_screen()
    {
        var admin = await Admin();
        var options = await Body(await admin.GetAsync($"{Url}/quick-add-options"));
        Assert.True(options.GetProperty("currencies").GetArrayLength() > 0);
        var currencyId = options.GetProperty("defaultCurrencyId").GetInt32();
        var name = $"Quick {Guid.NewGuid():N}"[..16];

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"{Url}/quick-add",
            new { name, description = "from test", generateBarcode = true, price = 2.5m, currencyId, stockQuantity = 12 }));
        var result = await Body(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var barcode = result.GetProperty("barcode").GetString()!;
        Assert.Equal(13, barcode.Length);
        Assert.Equal(DisplayOrderApiService.CheckDigit(barcode[..12]), barcode[12] - '0');

        var onScreen = (await Body(await admin.GetAsync(Url))).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetInt32() == result.GetProperty("displayOrderId").GetInt32());
        Assert.Equal(name, onScreen.GetProperty("name").GetString());
        Assert.Equal(2.5m, onScreen.GetProperty("price").GetDecimal());
        Assert.Equal(12m, onScreen.GetProperty("stockQuantity").GetDecimal());
        Assert.False(onScreen.GetProperty("isLowStock").GetBoolean(), "12 in stock against a minimum of 5");

        await admin.SendAsync(Json(HttpMethod.Delete, $"{Url}/items/{onScreen.GetProperty("id").GetInt32()}"));
    }

    [Fact]
    public async Task Quick_add_keeps_a_typed_barcode_unless_generation_is_asked_for()
    {
        var admin = await Admin();
        var typed = $"T{Guid.NewGuid():N}"[..12];

        var kept = await Body(await admin.SendAsync(Json(HttpMethod.Post, $"{Url}/quick-add", new { name = "Typed code", barcode = typed, price = 1 })));
        var replaced = await Body(await admin.SendAsync(Json(HttpMethod.Post, $"{Url}/quick-add", new { name = "Generated code", barcode = typed, generateBarcode = true, price = 1 })));

        Assert.Equal(typed, kept.GetProperty("barcode").GetString());
        Assert.NotEqual(typed, replaced.GetProperty("barcode").GetString());

        foreach (var r in new[] { kept, replaced })
            await admin.SendAsync(Json(HttpMethod.Delete, $"{Url}/items/{r.GetProperty("displayOrderId").GetInt32()}"));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("No price", 0)]
    [InlineData("Negative price", -3)]
    public async Task Quick_add_needs_a_name_and_a_price_above_zero(string name, decimal price)
    {
        var admin = await Admin();

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"{Url}/quick-add", new { name, price }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---- barcode recipe ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("400638133393", 1)] // well-known EAN-13 4006381333931
    [InlineData("590123412345", 7)] // 5901234123457
    public void The_check_digit_follows_ean13(string twelve, int expected)
        => Assert.Equal(expected, DisplayOrderApiService.CheckDigit(twelve));

    [Fact]
    public void A_generated_barcode_starts_with_2_and_the_date()
    {
        var barcode = DisplayOrderApiService.GenerateBarcode(new DateTime(2026, 10, 31), new Random(1));

        Assert.StartsWith("2261031", barcode);
        Assert.Equal(13, barcode.Length);
    }
}
