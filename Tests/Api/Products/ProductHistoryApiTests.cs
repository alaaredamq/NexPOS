using System.Net;
using System.Text.Json;
using Tests.Api.Infrastructure;
using Xunit;
using static Tests.Api.Pos.PosTestKit;

namespace Tests.Api.Products;

/// <summary>
/// <c>GET api/v1/products/{id}/history</c>: everything done to a product - opening stock, stock added and removed by hand, a sale at
/// the till - read back newest first, with who did it, what the stock was before and after, and lifetime totals.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProductHistoryApiTests
{
    private const int Usd = 1, Cash = 1;

    private readonly ApiFactory _factory;

    public ProductHistoryApiTests(ApiFactory factory) => _factory = factory;

    private async Task<int> CreateProductAsync(HttpClient admin, decimal stock)
    {
        var lookups = await Body(await admin.GetAsync("/api/v1/products/lookups"));
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", new Dictionary<string, object?>
        {
            ["name"] = $"Hist-{Guid.NewGuid():N}"[..20],
            ["description"] = "made by a history test",
            ["barcode"] = "H" + Guid.NewGuid().ToString("N")[..12],
            ["categoryId"] = lookups.GetProperty("categories")[0].GetProperty("id").GetInt32(),
            ["supplierId"] = lookups.GetProperty("suppliers")[0].GetProperty("id").GetInt32(),
            ["currencyId"] = 0,
            ["purchasePrice"] = 4m,
            ["sellingPrice"] = 6m,
            ["stockQuantity"] = stock,
            ["minStockLevel"] = 0m,
            ["isActive"] = true,
            ["unit"] = "piece",
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task The_history_lists_what_was_done_newest_first_with_stock_before_and_after()
    {
        var host = Selling(_factory);
        var (admin, _) = await SignedIn(_factory, ApiFactory.AdminUser, ApiFactory.AdminPin); // products are made on the main host, whose POS caches this clears
        var (till, _) = await Cashier(host); // only the till needs the host that may sell

        var id = await CreateProductAsync(admin, stock: 10);
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/{id}/adjust",
            new { type = "add", quantity = 5, reason = "Delivery from Acme" }))).StatusCode);

        var cart = Cart(Usd, [Line(id, quantity: 2)]);
        var total = (await Price(till, cart)).GetProperty("total").GetDecimal();
        var sale = await Body(await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/sales/checkout", Checkout(cart, total, Cash))));
        var orderNumber = $"ORD-{sale.GetProperty("orderId").GetInt32():D6}"; // the number the Orders pages show

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/{id}/adjust",
            new { type = "remove", quantity = 1, reason = "Damaged" }))).StatusCode);

        var history = await Body(await admin.GetAsync($"/api/v1/products/{id}/history"));

        Assert.Equal(12m, history.GetProperty("currentStock").GetDecimal());
        Assert.Equal(2m, history.GetProperty("totalSold").GetDecimal());
        Assert.Equal(5m, history.GetProperty("totalAdded").GetDecimal());
        Assert.Equal(0m, history.GetProperty("totalReturned").GetDecimal());

        var items = history.GetProperty("items");
        Assert.Equal(4, items.GetProperty("total").GetInt32());
        var rows = items.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["stockRemoved", "sold", "stockAdded", "created"], rows.Select(r => r.GetProperty("type").GetString()));

        var removed = rows[0];
        Assert.Equal(-1m, removed.GetProperty("quantityChange").GetDecimal());
        Assert.Equal(13m, removed.GetProperty("stockBefore").GetDecimal());
        Assert.Equal(12m, removed.GetProperty("stockAfter").GetDecimal());
        Assert.Equal("Damaged", removed.GetProperty("reason").GetString());

        var sold = rows[1];
        Assert.Equal(-2m, sold.GetProperty("quantityChange").GetDecimal());
        Assert.Equal(15m, sold.GetProperty("stockBefore").GetDecimal());
        Assert.Equal(13m, sold.GetProperty("stockAfter").GetDecimal());
        Assert.Equal(orderNumber, sold.GetProperty("orderNumber").GetString());
        Assert.False(string.IsNullOrEmpty(sold.GetProperty("userName").GetString()));

        var added = rows[2];
        Assert.Equal(5m, added.GetProperty("quantityChange").GetDecimal());
        Assert.Equal("Delivery from Acme", added.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrEmpty(added.GetProperty("userName").GetString()));

        Assert.Equal(10m, rows[3].GetProperty("stockAfter").GetDecimal());
    }

    [Fact]
    public async Task Cancelling_a_sale_adds_a_return_line_and_the_totals_follow()
    {
        var host = Selling(_factory);
        var (admin, _) = await SignedIn(_factory, ApiFactory.AdminUser, ApiFactory.AdminPin); // products are made on the main host, whose POS caches this clears
        var (till, _) = await Cashier(host); // only the till needs the host that may sell
        var id = await CreateProductAsync(admin, stock: 10);

        var cart = Cart(Usd, [Line(id, quantity: 3)]);
        var total = (await Price(till, cart)).GetProperty("total").GetDecimal();
        var sale = await Body(await till.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/sales/checkout", Checkout(cart, total, Cash))));
        var orderId = sale.GetProperty("orderId").GetInt32();

        var cancel = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/orders/{orderId}/status", new { status = 4 }));
        Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());

        var history = await Body(await admin.GetAsync($"/api/v1/products/{id}/history?kind=sales"));

        Assert.Equal(3m, history.GetProperty("totalSold").GetDecimal());
        Assert.Equal(3m, history.GetProperty("totalReturned").GetDecimal());
        var rows = history.GetProperty("items").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["saleReturned", "sold"], rows.Select(r => r.GetProperty("type").GetString())); // "sales" leaves out the opening stock
        Assert.Equal(3m, rows[0].GetProperty("quantityChange").GetDecimal());
        Assert.Equal(10m, rows[0].GetProperty("stockAfter").GetDecimal());
        Assert.Equal("Cancelled from the Orders page", rows[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Price_and_detail_edits_show_up_under_changes()
    {
        var (admin, _) = await SignedIn(_factory, ApiFactory.AdminUser, ApiFactory.AdminPin); // products are made on the main host, whose POS caches this clears
        var id = await CreateProductAsync(admin, stock: 4);

        var product = await Body(await admin.GetAsync($"/api/v1/products/{id}"));
        var edit = new Dictionary<string, object?>
        {
            ["name"] = product.GetProperty("name").GetString() + "x",
            ["description"] = product.GetProperty("description").GetString(),
            ["barcode"] = product.GetProperty("barcode").GetString(),
            ["categoryId"] = product.GetProperty("categoryId").GetInt32(),
            ["supplierId"] = product.GetProperty("supplierId").GetInt32(),
            ["currencyId"] = 0,
            ["purchasePrice"] = 4m,
            ["sellingPrice"] = 7.5m,
            ["stockQuantity"] = 4m,
            ["minStockLevel"] = 0m,
            ["isActive"] = true,
            ["unit"] = "piece",
        };
        var saved = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/products/{id}", edit));
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        var history = await Body(await admin.GetAsync($"/api/v1/products/{id}/history?kind=changes"));

        var rows = history.GetProperty("items").GetProperty("items").EnumerateArray().ToList();
        var price = Assert.Single(rows, r => r.GetProperty("type").GetString() == "sellingPriceChanged");
        Assert.Equal(6m, price.GetProperty("oldValue").GetDecimal());
        Assert.Equal(7.5m, price.GetProperty("newValue").GetDecimal());
        var details = Assert.Single(rows, r => r.GetProperty("type").GetString() == "updated");
        Assert.Equal(["name"], details.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        Assert.DoesNotContain(rows, r => r.GetProperty("type").GetString() is "created" or "stockSet");
    }

    [Fact]
    public async Task The_history_pages()
    {
        var (admin, _) = await SignedIn(_factory, ApiFactory.AdminUser, ApiFactory.AdminPin);
        var id = await CreateProductAsync(admin, stock: 1);
        for (var i = 0; i < 3; i++)
            await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/inventory/{id}/adjust", new { type = "add", quantity = 1 }));

        var page = (await Body(await admin.GetAsync($"/api/v1/products/{id}/history?page=2&pageSize=3"))).GetProperty("items");

        Assert.Equal(4, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.Equal("created", page.GetProperty("items")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task An_unknown_product_answers_404_and_a_signed_out_browser_401()
    {
        var (admin, _) = await SignedIn(_factory, ApiFactory.AdminUser, ApiFactory.AdminPin);

        await AssertProblem(await admin.GetAsync("/api/v1/products/999999/history"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await _factory.CreateBrowser().GetAsync("/api/v1/products/1/history"), HttpStatusCode.Unauthorized, "unauthenticated");
    }
}
