using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Web.Helpers;
using Web.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tests.Api.Infrastructure;
using Web.Api.Products;

namespace Tests.Api.Products;

/// <summary>
/// Track C1: the products API through the real pipeline - permission matrix (admin vs the cashier, who may only view),
/// the save rules (barcode uniqueness, extra-price floor, pack fields), stock and price persistence, soft delete,
/// list paging/filters/summary, barcode generation and the CSV import preview/apply.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProductsApiTests
{
    private readonly ApiFactory _factory;

    public ProductsApiTests(ApiFactory factory) => _factory = factory;

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static HttpRequestMessage Json(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-NexPOS-Client", "web");
        return request;
    }

    private async Task<HttpClient> SignedIn(string user, string pin, string? language = null)
    {
        var browser = _factory.CreateBrowser();
        var login = Json(HttpMethod.Post, "/api/v1/auth/login", new { username = user, pin });
        if (language != null) login.Headers.Add("Accept-Language", language);
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(login)).StatusCode);
        return browser;
    }

    private Task<HttpClient> Admin() => SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

    /// <summary>The seeded Admin role does not hold Products.Delete (only SuperAdmin does, as in the Razor app), so a delete test grants it first.</summary>
    private async Task<HttpClient> AdminWhoMayDelete()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleAccessService>();
            var admin = (await roles.ListAsync()).First(r => r.Name == PermissionHelper.Roles.Admin);
            if (!admin.Permissions.Contains(PermissionHelper.Permissions.Products_Delete))
                Assert.True((await roles.UpdateAsync(admin.Id, admin.Name, admin.Description, admin.Permissions.Append(PermissionHelper.Permissions.Products_Delete))).Success);
        }

        return await Admin();
    }

    private Task<HttpClient> Cashier() => SignedIn(ApiFactory.CashierUser, ApiFactory.CashierPin);

    private static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string UniqueBarcode() => "T" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>A valid request built from the shop's own first category/supplier, so the foreign keys exist.</summary>
    private static async Task<Dictionary<string, object?>> ValidRequest(HttpClient admin, string? barcode = null, string name = "API test product")
    {
        var lookups = await Body(await admin.GetAsync("/api/v1/products/lookups"));
        return new Dictionary<string, object?>
        {
            ["name"] = name,
            ["description"] = "made by a test",
            ["barcode"] = barcode ?? UniqueBarcode(),
            ["categoryId"] = lookups.GetProperty("categories")[0].GetProperty("id").GetInt32(),
            ["supplierId"] = lookups.GetProperty("suppliers")[0].GetProperty("id").GetInt32(),
            ["currencyId"] = 0,
            ["purchasePrice"] = 4.5m,
            ["sellingPrice"] = 7.25m,
            ["stockQuantity"] = 12m,
            ["minStockLevel"] = 2m,
            ["isActive"] = true,
            ["unit"] = "piece",
            ["priceTiers"] = new[] { new { name = "Wholesale", price = 6m } },
        };
    }

    private static async Task<JsonElement> Create(HttpClient admin, Dictionary<string, object?> request)
    {
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", request));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Body(response);
    }

    private static async Task<JsonElement> Errors(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await Body(response);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        return body.GetProperty("errors");
    }

    // ---- permission matrix -------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_every_products_endpoint_answers_401()
    {
        var anonymous = _factory.CreateBrowser();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/products/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Json(HttpMethod.Post, "/api/v1/products", new { }))).StatusCode);
    }

    [Fact]
    public async Task The_cashier_can_view_but_not_create_edit_delete_or_import()
    {
        var admin = await Admin();
        var cashier = await Cashier();
        var request = await ValidRequest(admin);
        var created = await Create(admin, request);
        var id = created.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/products/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync($"/api/v1/products/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/products/sticker-settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/products/export")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/products", request))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.SendAsync(Json(HttpMethod.Put, $"/api/v1/products/{id}", request))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.SendAsync(Json(HttpMethod.Delete, $"/api/v1/products/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/products/generate-barcode")).StatusCode);

        var form = new MultipartFormDataContent { { new StringContent("true"), "apply" } };
        var import = Json(HttpMethod.Post, "/api/v1/products/import");
        import.Content = form;
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.SendAsync(import)).StatusCode);
    }

    // ---- create / read / update -------------------------------------------------------------------------------

    [Fact]
    public async Task Create_saves_prices_stock_and_extra_prices_and_the_details_come_back_computed()
    {
        var admin = await Admin();
        var request = await ValidRequest(admin);

        var created = await Create(admin, request);

        Assert.Equal(7.25m, created.GetProperty("sellingPrice").GetDecimal());
        Assert.Equal(4.5m, created.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal(2.75m, created.GetProperty("profitMargin").GetDecimal());
        Assert.Equal(12m, created.GetProperty("stockQuantity").GetDecimal());
        Assert.Equal(87m, created.GetProperty("stockValue").GetDecimal()); // 7.25 x 12
        Assert.Equal(61.11m, Math.Round(created.GetProperty("profitMarginPercent").GetDecimal(), 2));
        Assert.Equal("ok", created.GetProperty("stockLevel").GetString());
        Assert.False(string.IsNullOrEmpty(created.GetProperty("currencyNotation").GetString())); // 0 = the default currency
        var tiers = created.GetProperty("priceTiers");
        Assert.Equal(1, tiers.GetArrayLength());
        Assert.Equal("Wholesale", tiers[0].GetProperty("name").GetString());

        var fetched = await Body(await admin.GetAsync($"/api/v1/products/{created.GetProperty("id").GetInt32()}"));
        Assert.Equal(request["barcode"], fetched.GetProperty("barcode").GetString());
    }

    [Fact]
    public async Task A_barcode_used_by_another_product_is_refused_on_create_and_on_edit_but_a_product_may_keep_its_own()
    {
        var admin = await Admin();
        var first = await Create(admin, await ValidRequest(admin));
        var firstBarcode = first.GetProperty("barcode").GetString()!;

        var duplicate = await ValidRequest(admin, barcode: firstBarcode, name: "Twin");
        var onCreate = await Errors(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", duplicate)));
        Assert.Equal("A product with this barcode already exists.", onCreate.GetProperty("barcode")[0].GetString());

        var second = await Create(admin, await ValidRequest(admin));
        var edit = await ValidRequest(admin, barcode: firstBarcode, name: "Second renamed");
        var onEdit = await Errors(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/products/{second.GetProperty("id").GetInt32()}", edit)));
        Assert.True(onEdit.TryGetProperty("barcode", out _));

        var keepOwn = await ValidRequest(admin, barcode: firstBarcode, name: "First renamed");
        var saved = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/products/{first.GetProperty("id").GetInt32()}", keepOwn));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("First renamed", (await Body(saved)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_extra_price_needs_a_name_an_amount_and_may_not_be_below_the_purchase_price_but_a_blank_row_is_ignored()
    {
        var admin = await Admin();
        var request = await ValidRequest(admin);
        request["purchasePrice"] = 5m;
        request["priceTiers"] = new object[]
        {
            new { name = "", price = 0m },          // blank: ignored
            new { name = "", price = 3m },          // no name
            new { name = "Bulk", price = 0m },      // no amount
            new { name = "Cheap", price = 4.99m },  // below cost
            new { name = "Fine", price = 5m },      // exactly the cost is allowed
        };

        var errors = await Errors(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", request)));

        Assert.False(errors.TryGetProperty("priceTiers[0].name", out _));
        Assert.Equal("Every extra price needs a name.", errors.GetProperty("priceTiers[1].name")[0].GetString());
        Assert.Equal("Every extra price needs an amount above 0.", errors.GetProperty("priceTiers[2].price")[0].GetString());
        Assert.Equal("An extra price cannot be below the purchase price.", errors.GetProperty("priceTiers[3].price")[0].GetString());
        Assert.False(errors.TryGetProperty("priceTiers[4].price", out _));
    }

    [Fact]
    public async Task Server_messages_come_back_in_the_language_of_the_request()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var request = await ValidRequest(admin);
        request["priceTiers"] = new[] { new { name = "", price = 3m } };

        var message = Json(HttpMethod.Post, "/api/v1/products", request);
        message.Headers.Add("Accept-Language", "ar");
        var errors = await Errors(await admin.SendAsync(message));

        Assert.Equal("كل سعر إضافي يحتاج إلى اسم.", errors.GetProperty("priceTiers[0].name")[0].GetString());
    }

    [Fact]
    public async Task Prices_must_be_above_zero_and_a_category_and_supplier_are_required()
    {
        var admin = await Admin();
        var request = await ValidRequest(admin);
        request["purchasePrice"] = 0m;
        request["sellingPrice"] = 0m;
        request["categoryId"] = 0;
        request["supplierId"] = 0;
        request["name"] = "x";

        var errors = await Errors(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", request)));

        foreach (var field in new[] { "purchasePrice", "sellingPrice", "categoryId", "supplierId", "name" })
            Assert.True(errors.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task A_pack_needs_its_size_and_single_product_and_an_update_never_changes_the_pack_setting()
    {
        var admin = await Admin();
        var single = await Create(admin, await ValidRequest(admin, name: "Single"));

        var bad = await ValidRequest(admin, name: "Six-pack");
        bad["isPack"] = true;
        var errors = await Errors(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", bad)));
        Assert.True(errors.TryGetProperty("packSize", out _));
        Assert.True(errors.TryGetProperty("singleProductId", out _));

        var good = await ValidRequest(admin, name: "Six-pack");
        good["isPack"] = true;
        good["packSize"] = 6;
        good["singleProductId"] = single.GetProperty("id").GetInt32();
        var pack = await Create(admin, good);
        Assert.True(pack.GetProperty("isPack").GetBoolean());
        Assert.Equal(6, pack.GetProperty("packSize").GetInt32());
        Assert.Equal("Single", pack.GetProperty("singleProductName").GetString());

        var edit = await ValidRequest(admin, barcode: pack.GetProperty("barcode").GetString(), name: "Six-pack v2");
        edit["isPack"] = false;
        var updated = await Body(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/products/{pack.GetProperty("id").GetInt32()}", edit)));
        Assert.True(updated.GetProperty("isPack").GetBoolean());
        Assert.Equal("Six-pack v2", updated.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_replaces_the_extra_prices_and_changes_stock_and_prices()
    {
        var admin = await Admin();
        var created = await Create(admin, await ValidRequest(admin));
        var id = created.GetProperty("id").GetInt32();

        var edit = await ValidRequest(admin, barcode: created.GetProperty("barcode").GetString());
        edit["sellingPrice"] = 9m;
        edit["stockQuantity"] = 0m;
        edit["priceTiers"] = Array.Empty<object>();
        var updated = await Body(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/products/{id}", edit)));

        Assert.Equal(9m, updated.GetProperty("sellingPrice").GetDecimal());
        Assert.Equal(0m, updated.GetProperty("stockQuantity").GetDecimal());
        Assert.Equal("out", updated.GetProperty("stockLevel").GetString());
        Assert.Equal(0, updated.GetProperty("priceTiers").GetArrayLength());
    }

    [Fact]
    public async Task Unknown_products_answer_404_for_get_update_and_delete()
    {
        var admin = await AdminWhoMayDelete();
        var request = await ValidRequest(admin);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/products/987654")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Json(HttpMethod.Put, "/api/v1/products/987654", request))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Json(HttpMethod.Delete, "/api/v1/products/987654"))).StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_product_from_the_list_and_a_second_delete_is_404()
    {
        var admin = await AdminWhoMayDelete();
        var created = await Create(admin, await ValidRequest(admin));
        var id = created.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/products/{id}"))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/products/{id}")).StatusCode);
        var list = await Body(await admin.GetAsync($"/api/v1/products?q={created.GetProperty("barcode").GetString()}"));
        Assert.Equal(0, list.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/products/{id}"))).StatusCode);
    }

    // ---- list, filters, summary -------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_pages_sorts_searches_and_filters_by_stock_and_status()
    {
        var admin = await Admin();
        var tag = Guid.NewGuid().ToString("N")[..8];
        async Task<int> Add(string name, decimal stock, bool active, decimal price)
        {
            var r = await ValidRequest(admin, name: $"{tag}-{name}");
            r["stockQuantity"] = stock;
            r["isActive"] = active;
            r["sellingPrice"] = price;
            return (await Create(admin, r)).GetProperty("id").GetInt32();
        }

        await Add("bravo", stock: 3m, active: true, price: 20m);   // low
        await Add("alpha", stock: 0m, active: true, price: 10m);   // out
        await Add("charlie", stock: 50m, active: false, price: 30m); // ok, inactive

        var all = await Body(await admin.GetAsync($"/api/v1/products?q={tag}&sort=name&dir=asc"));
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.Equal(new[] { $"{tag}-alpha", $"{tag}-bravo", $"{tag}-charlie" },
            all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray());

        var byPriceDesc = await Body(await admin.GetAsync($"/api/v1/products?q={tag}&sort=sellingPrice&dir=desc"));
        Assert.Equal($"{tag}-charlie", byPriceDesc.GetProperty("items")[0].GetProperty("name").GetString());

        var page2 = await Body(await admin.GetAsync($"/api/v1/products?q={tag}&sort=name&page=2&pageSize=2"));
        Assert.Equal(3, page2.GetProperty("total").GetInt32());
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());

        var low = await Body(await admin.GetAsync($"/api/v1/products?q={tag}&stock=low"));
        Assert.Equal(1, low.GetProperty("total").GetInt32());
        Assert.Equal("low", low.GetProperty("items")[0].GetProperty("stockLevel").GetString());
        Assert.Equal(1, (await Body(await admin.GetAsync($"/api/v1/products?q={tag}&stock=out"))).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Body(await admin.GetAsync($"/api/v1/products?q={tag}&status=inactive"))).GetProperty("total").GetInt32());
        Assert.Equal(2, (await Body(await admin.GetAsync($"/api/v1/products?q={tag}&status=active"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task The_search_also_matches_the_description()
    {
        var admin = await Admin();
        var marker = "zq" + Guid.NewGuid().ToString("N")[..8];
        var request = await ValidRequest(admin);
        request["description"] = $"contains {marker} inside";
        await Create(admin, request);

        var found = await Body(await admin.GetAsync($"/api/v1/products?q={marker}"));

        Assert.Equal(1, found.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task The_summary_counts_the_whole_catalogue_with_the_same_stock_rule_as_the_list()
    {
        var admin = await Admin();
        var before = await Body(await admin.GetAsync("/api/v1/products/summary"));
        var r = await ValidRequest(admin);
        r["stockQuantity"] = 5m; // 5 is still "low": above 0 and up to the threshold
        await Create(admin, r);
        var r2 = await ValidRequest(admin);
        r2["stockQuantity"] = 0m;
        r2["isActive"] = false;
        await Create(admin, r2);

        var after = await Body(await admin.GetAsync("/api/v1/products/summary"));

        Assert.Equal(before.GetProperty("total").GetInt32() + 2, after.GetProperty("total").GetInt32());
        Assert.Equal(before.GetProperty("active").GetInt32() + 1, after.GetProperty("active").GetInt32());
        Assert.Equal(before.GetProperty("lowStock").GetInt32() + 1, after.GetProperty("lowStock").GetInt32());
        Assert.Equal(before.GetProperty("outOfStock").GetInt32() + 1, after.GetProperty("outOfStock").GetInt32());
        Assert.Equal(5m, after.GetProperty("lowStockThreshold").GetDecimal());
    }

    [Fact]
    public async Task Paging_arguments_are_validated()
    {
        var admin = await Admin();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/products?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/products?pageSize=1000")).StatusCode);
    }

    // ---- lookups, options, barcode ----------------------------------------------------------------------------

    [Fact]
    public async Task Lookups_list_active_categories_suppliers_currencies_and_the_defaults()
    {
        var admin = await Admin();

        var lookups = await Body(await admin.GetAsync("/api/v1/products/lookups"));

        Assert.True(lookups.GetProperty("categories").GetArrayLength() > 0);
        Assert.True(lookups.GetProperty("suppliers").GetArrayLength() > 0);
        Assert.True(lookups.GetProperty("currencies").GetArrayLength() > 0);
        Assert.NotEqual(JsonValueKind.Null, lookups.GetProperty("defaultSupplierId").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, lookups.GetProperty("defaultCurrencyId").ValueKind);
    }

    [Fact]
    public async Task Options_find_active_products_by_name_or_barcode()
    {
        var admin = await Admin();
        var created = await Create(admin, await ValidRequest(admin, name: "Findable pack target"));

        var byName = await Body(await admin.GetAsync("/api/v1/products/options?q=Findable pack"));
        var byBarcode = await Body(await admin.GetAsync($"/api/v1/products/options?q={created.GetProperty("barcode").GetString()}"));

        Assert.Contains(byName.EnumerateArray(), o => o.GetProperty("id").GetInt32() == created.GetProperty("id").GetInt32());
        Assert.Equal(1, byBarcode.GetArrayLength());
    }

    [Fact]
    public async Task Generated_barcodes_are_valid_unused_ean13_in_the_store_range()
    {
        var admin = await Admin();

        var barcode = (await Body(await admin.GetAsync("/api/v1/products/generate-barcode"))).GetProperty("barcode").GetString()!;

        Assert.Equal(13, barcode.Length);
        Assert.All(barcode, c => Assert.True(char.IsDigit(c)));
        Assert.StartsWith("2", barcode);
        Assert.Equal(ProductBarcode.CheckDigit(barcode[..12]), barcode[12] - '0');
    }

    [Theory]
    [InlineData("200000000000", 8)]
    [InlineData("400638133393", 1)] // 4006381333931 is the textbook EAN-13
    [InlineData("590123412345", 7)] // 5901234123457
    public void The_ean13_check_digit_matches_the_standard(string first12, int expected)
        => Assert.Equal(expected, ProductBarcode.CheckDigit(first12));

    // ---- labels, export, import -------------------------------------------------------------------------------

    [Fact]
    public async Task Sticker_settings_default_to_the_shops_label_size()
    {
        var admin = await Admin();

        var settings = await Body(await admin.GetAsync("/api/v1/products/sticker-settings"));

        Assert.InRange(settings.GetProperty("widthMm").GetInt32(), 15, 150);
        Assert.InRange(settings.GetProperty("heightMm").GetInt32(), 10, 150);
    }

    [Fact]
    public async Task The_sticker_pdf_is_a_pdf_and_unusable_bars_are_refused()
    {
        var admin = await Admin();
        object Label(string bars) => new { name = "Cable", code = "2261013123452", price = "$4.00", bars, widthMm = 40, heightMm = 25, copies = 2, showPrice = true };

        var ok = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products/sticker-pdf", Label("1101001101")));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/pdf", ok.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await ok.Content.ReadAsByteArrayAsync()).Take(4).ToArray()));

        var bad = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products/sticker-pdf", Label("abc")));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Export_returns_a_csv_with_the_products_and_the_template_has_the_headers()
    {
        var admin = await Admin();
        var created = await Create(admin, await ValidRequest(admin, name: "Exported one"));

        var export = await admin.GetAsync("/api/v1/products/export");
        var template = await admin.GetAsync("/api/v1/products/import-template");

        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains(created.GetProperty("barcode").GetString()!, await export.Content.ReadAsStringAsync());
        Assert.Contains("import_template", template.Content.Headers.ContentDisposition?.FileName ?? "");
        Assert.Contains("Barcode", await template.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequestMessage ImportRequest(string csv, bool apply)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var form = new MultipartFormDataContent { { file, "file", "products.csv" }, { new StringContent(apply ? "true" : "false"), "apply" } };
        var request = Json(HttpMethod.Post, "/api/v1/products/import");
        request.Content = form;
        return request;
    }

    [Fact]
    public async Task Import_preview_saves_nothing_and_apply_creates_the_new_rows_and_updates_existing_barcodes()
    {
        var admin = await Admin();
        var existing = await Create(admin, await ValidRequest(admin, name: "Before import"));
        var existingBarcode = existing.GetProperty("barcode").GetString()!;
        var newBarcode = UniqueBarcode();
        var supplier = (await Body(await admin.GetAsync("/api/v1/products/lookups"))).GetProperty("suppliers")[0].GetProperty("name").GetString();
        var csv = string.Join("\n",
            "Barcode,Name,Category,Supplier,Selling Price",
            $"{existingBarcode},After import,,,11.5",
            $"{newBarcode},Brand new,Import test category,{supplier},3") + "\n";

        var preview = await Body(await admin.SendAsync(ImportRequest(csv, apply: false)));
        Assert.False(preview.GetProperty("applied").GetBoolean());
        Assert.Equal(1, preview.GetProperty("created").GetInt32());
        Assert.Equal(1, preview.GetProperty("updated").GetInt32());
        Assert.Equal(1, preview.GetProperty("newCategories").GetInt32());
        Assert.Equal(1, preview.GetProperty("newCategories").GetInt32());
        Assert.Equal(0, (await Body(await admin.GetAsync($"/api/v1/products?q={newBarcode}"))).GetProperty("total").GetInt32());

        var applied = await Body(await admin.SendAsync(ImportRequest(csv, apply: true)));
        Assert.True(applied.GetProperty("applied").GetBoolean());
        Assert.Equal(1, applied.GetProperty("created").GetInt32());
        Assert.Equal(1, (await Body(await admin.GetAsync($"/api/v1/products?q={newBarcode}"))).GetProperty("total").GetInt32());
        var reread = await Body(await admin.GetAsync($"/api/v1/products/{existing.GetProperty("id").GetInt32()}"));
        Assert.Equal("After import", reread.GetProperty("name").GetString());
        Assert.Equal(11.5m, reread.GetProperty("sellingPrice").GetDecimal());
    }

    [Fact]
    public async Task Import_without_a_file_is_a_validation_error_and_an_unreadable_file_is_reported_in_the_body()
    {
        var admin = await Admin();
        var noFile = Json(HttpMethod.Post, "/api/v1/products/import");
        noFile.Content = new MultipartFormDataContent { { new StringContent("false"), "apply" } };

        var errors = await Errors(await admin.SendAsync(noFile));
        Assert.True(errors.TryGetProperty("file", out _));

        var unreadable = await admin.SendAsync(ImportRequest("", apply: false));
        Assert.Equal(HttpStatusCode.BadRequest, unreadable.StatusCode); // empty file = no file

        var noHeader = await Body(await admin.SendAsync(ImportRequest("just some words\n", apply: false)));
        Assert.False(string.IsNullOrEmpty(noHeader.GetProperty("fileError").GetString()));
    }
}
