using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sync;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Web.Api.Products;
using Web.Api.Suppliers;
using Xunit;

namespace Tests.Api.Online;

/// <summary>
/// The shop-PC side of "add it from the phone": each handler is run exactly as the background service runs it (a fresh scope, the
/// payload as JSON) against the real till services and database, then the result is read back through the till's own API.
/// How commands travel is covered in Cloud.Tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OnlineCommandHandlerTests : CatalogApiTestBase
{
    public OnlineCommandHandlerTests(ApiFactory factory) : base(factory)
    {
    }

    private async Task<CommandResult> RunAsync(string type, object payload, string by = "Layla")
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetServices<IOnlineCommandHandler>().Single(h => h.Type == type);
        var json = JsonSerializer.SerializeToElement(payload, SyncWire.Json);
        return await handler.HandleAsync(json, new CommandContext(Guid.NewGuid(), by), default);
    }

    private static string Unique(string stem) => stem + Guid.NewGuid().ToString("N")[..6];

    [Fact]
    public async Task The_shop_knows_every_kind_of_change_the_cloud_can_queue()
    {
        using var scope = Factory.Services.CreateScope();
        var types = scope.ServiceProvider.GetServices<IOnlineCommandHandler>().Select(h => h.Type).ToHashSet();

        Assert.Superset(new HashSet<string> { "customer.create", "supplier.create", "product.create" }, types);
    }

    [Fact]
    public async Task A_customer_added_from_the_phone_appears_in_the_tills_customer_list()
    {
        var first = Unique("Nadine");

        var result = await RunAsync("customer.create", new { firstName = first, lastName = "Fares", phoneNumber = "71234567" });

        Assert.True(result.IsApplied, result.Message);
        var list = await Body(await (await Admin()).GetAsync($"/api/v1/customers?q={first}"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(first, list.GetProperty("items")[0].GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task A_customer_with_a_missing_or_too_short_name_is_refused_and_nothing_is_saved()
    {
        var last = Unique("Nobody");

        var result = await RunAsync("customer.create", new { firstName = "N", lastName = last });

        Assert.Equal(CommandResult.Rejected, result.Outcome);
        Assert.Equal(CommandErrorCodes.Validation, result.Code);
        Assert.True(result.Data!.Value.GetProperty("errors").TryGetProperty("firstName", out _));
        var list = await Body(await (await Admin()).GetAsync($"/api/v1/customers?q={last}"));
        Assert.Equal(0, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_body_that_is_not_a_request_is_refused_as_a_bad_payload()
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetServices<IOnlineCommandHandler>().Single(h => h.Type == "customer.create");

        var result = await handler.HandleAsync(JsonDocument.Parse("""["not","an","object"]""").RootElement, new CommandContext(Guid.NewGuid(), "Layla"), default);

        Assert.Equal(CommandErrorCodes.BadPayload, result.Code);
    }

    [Fact]
    public async Task A_supplier_added_from_the_phone_appears_in_the_tills_supplier_list()
    {
        int currencyId;
        using (var scope = Factory.Services.CreateScope())
            currencyId = (await scope.ServiceProvider.GetRequiredService<ISupplierApiService>().OptionsAsync()).Currencies.First().Id;
        var name = Unique("Mazraa Bakery ");

        var result = await RunAsync("supplier.create", new { name, phone = "03456789", currencyId });

        Assert.True(result.IsApplied, result.Message);
        var list = await Body(await (await Admin()).GetAsync($"/api/v1/suppliers?q={Uri.EscapeDataString(name)}"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_supplier_with_an_unknown_currency_is_refused()
    {
        var result = await RunAsync("supplier.create", new { name = Unique("Ghost "), currencyId = 99999 });

        Assert.Equal(CommandErrorCodes.Validation, result.Code);
        Assert.True(result.Data!.Value.GetProperty("errors").TryGetProperty("currencyId", out _));
    }

    private async Task<(int CategoryId, int SupplierId)> LookupAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var lookups = await scope.ServiceProvider.GetRequiredService<IProductCatalogService>().LookupsAsync();
        return (lookups.Categories.First().Id, lookups.DefaultSupplierId ?? lookups.Suppliers.First().Id);
    }

    [Fact]
    public async Task A_product_added_from_the_phone_is_on_sale_with_its_stock_and_its_history_names_who_asked()
    {
        var (categoryId, supplierId) = await LookupAsync();
        var name = Unique("Labneh ");
        var barcode = "29" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);

        var result = await RunAsync("product.create", new
        {
            name, barcode, categoryId, supplierId, purchasePrice = 3.8m, sellingPrice = 5.5m, stockQuantity = 24, minStockLevel = 12, unit = "Piece",
        });

        Assert.True(result.IsApplied, result.Message);
        var id = result.Data!.Value.GetProperty("id").GetInt32();
        var admin = await Admin();
        var product = await Body(await admin.GetAsync($"/api/v1/products/{id}"));
        Assert.Equal(name, product.GetProperty("name").GetString());
        Assert.Equal(24m, product.GetProperty("stockQuantity").GetDecimal());

        var history = await (await admin.GetAsync($"/api/v1/products/{id}/history")).Content.ReadAsStringAsync();
        Assert.Contains("Mobile · Layla", history);
    }

    [Fact]
    public async Task A_second_product_with_the_same_barcode_is_refused_like_at_the_till()
    {
        var (categoryId, supplierId) = await LookupAsync();
        var barcode = "28" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);
        object Product(string name) => new { name, barcode, categoryId, supplierId, purchasePrice = 1m, sellingPrice = 2m };

        Assert.True((await RunAsync("product.create", Product(Unique("First ")))).IsApplied);
        var second = await RunAsync("product.create", Product(Unique("Second ")));

        Assert.Equal(CommandErrorCodes.Validation, second.Code);
        Assert.True(second.Data!.Value.GetProperty("errors").TryGetProperty("barcode", out _));
    }

    [Fact]
    public async Task A_product_with_no_selling_price_is_refused_with_the_field_named()
    {
        var (categoryId, supplierId) = await LookupAsync();

        var result = await RunAsync("product.create", new { name = Unique("Free "), barcode = Unique("B"), categoryId, supplierId, purchasePrice = 1m });

        Assert.Equal(CommandErrorCodes.Validation, result.Code);
        Assert.True(result.Data!.Value.GetProperty("errors").TryGetProperty("sellingPrice", out _));
    }
    private async Task<(int Id, string Name)> NewProductAsync(decimal stock = 10, decimal selling = 5m)
    {
        var (categoryId, supplierId) = await LookupAsync();
        var name = Unique("Edit me ");
        var barcode = "27" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);
        var created = await RunAsync("product.create", new { name, barcode, categoryId, supplierId, purchasePrice = 2m, sellingPrice = selling, stockQuantity = stock, minStockLevel = 3 });
        Assert.True(created.IsApplied, created.Message);
        return (created.Data!.Value.GetProperty("id").GetInt32(), name);
    }

    private async Task<JsonElement> ProductAsync(int id) => await Body(await (await Admin()).GetAsync($"/api/v1/products/{id}"));

    [Fact]
    public async Task The_shop_knows_the_edit_and_stock_changes_too()
    {
        using var scope = Factory.Services.CreateScope();
        var types = scope.ServiceProvider.GetServices<IOnlineCommandHandler>().Select(h => h.Type).ToHashSet();

        Assert.Superset(new HashSet<string> { "product.update", "stock.adjust" }, types);
    }

    [Fact]
    public async Task Changing_a_price_from_the_phone_changes_only_the_price()
    {
        var (id, name) = await NewProductAsync(stock: 10, selling: 5m);
        var before = await ProductAsync(id);

        var result = await RunAsync("product.update", new { productId = id, sellingPrice = 6.25m });

        Assert.True(result.IsApplied, result.Message);
        var after = await ProductAsync(id);
        Assert.Equal(6.25m, after.GetProperty("sellingPrice").GetDecimal());
        Assert.Equal(name, after.GetProperty("name").GetString());
        Assert.Equal(before.GetProperty("barcode").GetString(), after.GetProperty("barcode").GetString());
        Assert.Equal(10m, after.GetProperty("stockQuantity").GetDecimal());
        Assert.Equal(before.GetProperty("categoryId").GetInt32(), after.GetProperty("categoryId").GetInt32());
    }

    [Fact]
    public async Task A_price_below_cost_rules_and_unknown_products_are_refused_with_a_reason()
    {
        var (id, _) = await NewProductAsync();

        var zero = await RunAsync("product.update", new { productId = id, sellingPrice = 0m });
        Assert.Equal(CommandErrorCodes.Validation, zero.Code);
        Assert.True(zero.Data!.Value.GetProperty("errors").TryGetProperty("sellingPrice", out _));

        var gone = await RunAsync("product.update", new { productId = 987654, sellingPrice = 3m });
        Assert.Equal(CommandErrorCodes.NotFound, gone.Code);
    }

    [Fact]
    public async Task Stopping_the_sale_of_a_product_from_the_phone_marks_it_inactive()
    {
        var (id, _) = await NewProductAsync();

        Assert.True((await RunAsync("product.update", new { productId = id, isActive = false })).IsApplied);

        Assert.False((await ProductAsync(id)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Adding_and_removing_stock_from_the_phone_moves_the_stock_and_the_history_names_who_asked()
    {
        var (id, _) = await NewProductAsync(stock: 10);

        var added = await RunAsync("stock.adjust", new { productId = id, type = "add", quantity = 5, reason = "Delivery" });
        Assert.True(added.IsApplied, added.Message);
        Assert.Equal(15m, added.Data!.Value.GetProperty("now").GetDecimal());

        var removed = await RunAsync("stock.adjust", new { productId = id, type = "remove", quantity = 2 });
        Assert.Equal(13m, removed.Data!.Value.GetProperty("now").GetDecimal());

        Assert.Equal(13m, (await ProductAsync(id)).GetProperty("stockQuantity").GetDecimal());
        var history = await (await (await Admin()).GetAsync($"/api/v1/products/{id}/history")).Content.ReadAsStringAsync();
        Assert.Contains("Mobile · Layla", history);
    }

    [Fact]
    public async Task A_stock_count_sets_the_stock_to_the_counted_figure_even_when_it_went_down_or_up()
    {
        var (id, _) = await NewProductAsync(stock: 10);

        var down = await RunAsync("stock.adjust", new { productId = id, type = "set", quantity = 4 });
        Assert.True(down.IsApplied, down.Message);
        Assert.Equal(4m, (await ProductAsync(id)).GetProperty("stockQuantity").GetDecimal());

        var up = await RunAsync("stock.adjust", new { productId = id, type = "set", quantity = 30 });
        Assert.Equal(30m, up.Data!.Value.GetProperty("now").GetDecimal());
        Assert.Equal(30m, (await ProductAsync(id)).GetProperty("stockQuantity").GetDecimal());

        var same = await RunAsync("stock.adjust", new { productId = id, type = "set", quantity = 30 });
        Assert.True(same.IsApplied);
        Assert.Equal(30m, (await ProductAsync(id)).GetProperty("stockQuantity").GetDecimal());
    }

    [Fact]
    public async Task Removing_more_than_there_is_is_refused_and_nothing_changes()
    {
        var (id, _) = await NewProductAsync(stock: 3);

        var result = await RunAsync("stock.adjust", new { productId = id, type = "remove", quantity = 8 });

        Assert.Equal(CommandErrorCodes.Validation, result.Code);
        Assert.True(result.Data!.Value.GetProperty("errors").TryGetProperty("quantity", out _));
        Assert.Equal(3m, (await ProductAsync(id)).GetProperty("stockQuantity").GetDecimal());
    }

    [Fact]
    public async Task A_stock_change_with_a_bad_type_or_an_unknown_product_is_refused()
    {
        var (id, _) = await NewProductAsync();

        Assert.Equal(CommandErrorCodes.Validation, (await RunAsync("stock.adjust", new { productId = id, type = "steal", quantity = 1 })).Code);
        Assert.Equal(CommandErrorCodes.NotFound, (await RunAsync("stock.adjust", new { productId = 987654, type = "add", quantity = 1 })).Code);
    }
}
