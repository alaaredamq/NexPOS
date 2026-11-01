using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Web.Helpers;
using Web.Services;
using Xunit;

namespace Tests.Api.Suppliers;

/// <summary>
/// Suppliers API (track C3): the permission matrix of the old controller, CRUD, and above all the debt ledger -
/// the balance is a running total that must equal the sum of the entries, and a supplier that still owes (or is owed)
/// money cannot be deleted.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SuppliersApiTests : CatalogApiTestBase
{
    public SuppliersApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..22];

    private static async Task<int> CurrencyIdAsync(HttpClient client)
        => (await Body(await client.GetAsync("/api/v1/suppliers/options"))).GetProperty("defaultCurrencyId").GetInt32();

    private static async Task<(int Id, string Name)> CreateAsync(HttpClient admin, bool isActive = true)
    {
        var name = Unique("Sup");
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["email"] = $"{Guid.NewGuid():N}@example.com",
            ["phone"] = "70123456",
            ["currencyId"] = await CurrencyIdAsync(admin),
            ["discountRate"] = 5,
            ["isActive"] = isActive,
        };
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/suppliers", body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await Body(response)).GetProperty("id").GetInt32(), name);
    }

    private static Task<HttpResponseMessage> AddDebt(HttpClient client, int id, decimal amount, bool iOweMore, string? note = null)
        => client.SendAsync(Json(HttpMethod.Post, $"/api/v1/suppliers/{id}/debt-transactions", new { amount, iOweMore, note }));

    private static async Task<decimal> BalanceAsync(HttpClient client, int id)
        => (await Body(await client.GetAsync($"/api/v1/suppliers/{id}"))).GetProperty("balance").GetDecimal();

    /// <summary>The shipped Admin role holds no <c>Suppliers.Delete</c> (like Categories.Delete): grant it, which also proves a role edit reaches an open session.</summary>
    private async Task GrantAdminSupplierDeleteAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleAccessService>();
        var admin = (await roles.ListAsync()).Single(r => r.Name == PermissionHelper.Roles.Admin);
        if (admin.Permissions.Contains(PermissionHelper.Permissions.Suppliers_Delete)) return;

        var result = await roles.UpdateAsync(admin.Id, admin.Name, admin.Description, admin.Permissions.Append(PermissionHelper.Permissions.Suppliers_Delete));
        Assert.True(result.Success, result.Error);
    }

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_the_list_answers_401()
    {
        await AssertProblem(await Factory.CreateBrowser().GetAsync("/api/v1/suppliers"), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_holds_no_supplier_permission()
    {
        var cashier = await Cashier();

        foreach (var url in new[] { "/api/v1/suppliers", "/api/v1/suppliers/summary", "/api/v1/suppliers/options", "/api/v1/suppliers/1", "/api/v1/suppliers/1/debt-transactions", "/api/v1/suppliers/export" })
            await AssertProblem(await cashier.GetAsync(url), HttpStatusCode.Forbidden, "forbidden");

        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/suppliers", new { name = "Nope", currencyId = 1 })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Put, "/api/v1/suppliers/1", new { name = "Nope", currencyId = 1 })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Delete, "/api/v1/suppliers/1")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await AddDebt(cashier, 1, 5, true), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task An_admin_without_suppliers_delete_gets_403_until_the_role_is_edited()
    {
        var admin = await Admin();
        var (id, _) = await CreateAsync(admin);

        // Not granted yet unless another test ran first - the grant is permanent for the shared test database.
        using (var scope = Factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleAccessService>();
            var role = (await roles.ListAsync()).Single(r => r.Name == PermissionHelper.Roles.Admin);
            if (role.Permissions.Contains(PermissionHelper.Permissions.Suppliers_Delete))
                return;
        }

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/suppliers/{id}")), HttpStatusCode.Forbidden, "forbidden");

        await GrantAdminSupplierDeleteAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/suppliers/{id}"))).StatusCode);
    }

    // ---- CRUD --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_supplier_can_be_created_read_edited_and_starts_at_zero_balance()
    {
        var admin = await Admin();
        var (id, name) = await CreateAsync(admin);

        var read = await Body(await admin.GetAsync($"/api/v1/suppliers/{id}"));
        Assert.Equal(name, read.GetProperty("name").GetString());
        Assert.Equal(0m, read.GetProperty("balance").GetDecimal());
        Assert.Equal(5d, read.GetProperty("discountRate").GetDouble());
        Assert.False(string.IsNullOrEmpty(read.GetProperty("currencyNotation").GetString()));
        Assert.True(read.GetProperty("isActive").GetBoolean());

        var edited = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/suppliers/{id}", new
        {
            name = name + " Ltd",
            email = (string?)null,
            phone = "03111222",
            city = "Beirut",
            currencyId = read.GetProperty("currencyId").GetInt32(),
            discountRate = 12.5,
            isActive = false,
        }));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var body = await Body(edited);
        Assert.Equal(name + " Ltd", body.GetProperty("name").GetString());
        Assert.Equal("Beirut", body.GetProperty("city").GetString());
        Assert.Equal(12.5d, body.GetProperty("discountRate").GetDouble());
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task Editing_a_supplier_never_touches_its_balance()
    {
        var admin = await Admin();
        var (id, name) = await CreateAsync(admin);
        Assert.Equal(HttpStatusCode.Created, (await AddDebt(admin, id, 40, true)).StatusCode);

        var edited = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/suppliers/{id}", new { name, currencyId = await CurrencyIdAsync(admin), discountRate = 0 }));

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(40m, await BalanceAsync(admin, id));
    }

    [Fact]
    public async Task The_form_rules_are_field_errors()
    {
        var admin = await Admin();
        var currency = await CurrencyIdAsync(admin);

        async Task<JsonElement> Errors(object body)
        {
            var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/suppliers", body));
            await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
            return (await Body(response)).GetProperty("errors");
        }

        Assert.True((await Errors(new { name = "x", currencyId = currency })).TryGetProperty("name", out _));
        Assert.True((await Errors(new { name = "Valid name", email = "not-an-email", currencyId = currency })).TryGetProperty("email", out _));
        Assert.True((await Errors(new { name = "Valid name", discountRate = 101, currencyId = currency })).TryGetProperty("discountRate", out _));
        Assert.True((await Errors(new { name = "Valid name", currencyId = 987654 })).TryGetProperty("currencyId", out _)); // a currency that is not in the shop
    }

    [Fact]
    public async Task An_unknown_supplier_is_a_404_everywhere()
    {
        var admin = await Admin();

        await AssertProblem(await admin.GetAsync("/api/v1/suppliers/999999"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.GetAsync("/api/v1/suppliers/999999/debt-transactions"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await AddDebt(admin, 999999, 5, true), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Put, "/api/v1/suppliers/999999", new { name = "Ghost", currencyId = await CurrencyIdAsync(admin) })), HttpStatusCode.NotFound, "not_found");
    }

    // ---- list --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_pages_sorts_searches_and_shows_balances()
    {
        var admin = await Admin();
        var (id, name) = await CreateAsync(admin);
        await CreateAsync(admin);
        await AddDebt(admin, id, 12.5m, true);

        var found = await Body(await admin.GetAsync($"/api/v1/suppliers?q={name}"));
        Assert.Equal(1, found.GetProperty("total").GetInt32());
        Assert.Equal(12.5m, found.GetProperty("items")[0].GetProperty("balance").GetDecimal());

        var byBalance = await Body(await admin.GetAsync("/api/v1/suppliers?sort=balance&dir=desc&pageSize=200"));
        var balances = byBalance.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("balance").GetDecimal()).ToList();
        Assert.Equal(balances.OrderByDescending(b => b), balances);

        var paged = await Body(await admin.GetAsync("/api/v1/suppliers?pageSize=1&page=2"));
        Assert.Equal(1, paged.GetProperty("items").GetArrayLength());
        Assert.True(paged.GetProperty("total").GetInt32() >= 2);
    }

    [Fact]
    public async Task The_summary_counts_the_whole_table()
    {
        var admin = await Admin();
        var (id, _) = await CreateAsync(admin);
        await CreateAsync(admin, isActive: false);
        await AddDebt(admin, id, 3, true);

        var all = (await Body(await admin.GetAsync("/api/v1/suppliers?pageSize=200"))).GetProperty("items").EnumerateArray().ToList();
        var summary = await Body(await admin.GetAsync("/api/v1/suppliers/summary"));

        Assert.Equal(all.Count, summary.GetProperty("total").GetInt32());
        Assert.Equal(all.Count(s => s.GetProperty("isActive").GetBoolean()), summary.GetProperty("active").GetInt32());
        Assert.Equal(all.Count(s => !s.GetProperty("isActive").GetBoolean()), summary.GetProperty("inactive").GetInt32());
        Assert.Equal(all.Count(s => s.GetProperty("balance").GetDecimal() != 0), summary.GetProperty("withBalance").GetInt32());
    }

    [Fact]
    public async Task The_product_count_is_the_number_of_products_pointing_at_the_supplier()
    {
        var admin = await Admin();
        var (id, name) = await CreateAsync(admin);
        var lookups = await Body(await admin.GetAsync("/api/v1/products/lookups"));
        var create = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", new Dictionary<string, object?>
        {
            ["name"] = Unique("SupProd"),
            ["barcode"] = "S" + Guid.NewGuid().ToString("N")[..12],
            ["categoryId"] = lookups.GetProperty("categories")[0].GetProperty("id").GetInt32(),
            ["supplierId"] = id,
            ["currencyId"] = 0,
            ["purchasePrice"] = 1m,
            ["sellingPrice"] = 2m,
            ["stockQuantity"] = 1m,
            ["isActive"] = true,
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        Assert.Equal(1, (await Body(await admin.GetAsync($"/api/v1/suppliers/{id}"))).GetProperty("productCount").GetInt32());
        var inList = (await Body(await admin.GetAsync($"/api/v1/suppliers?q={name}"))).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(1, inList.GetProperty("productCount").GetInt32());
        var byProducts = (await Body(await admin.GetAsync("/api/v1/suppliers?sort=products&dir=desc&pageSize=200"))).GetProperty("items").EnumerateArray().Select(r => r.GetProperty("productCount").GetInt32()).ToList();
        Assert.Equal(byProducts.OrderByDescending(c => c), byProducts);
    }

    // ---- debt ledger -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Ledger_entries_move_the_balance_both_ways_and_the_balance_equals_their_sum()
    {
        var admin = await Admin();
        var (id, _) = await CreateAsync(admin);

        var first = await AddDebt(admin, id, 100m, iOweMore: true, note: "Delivery 1");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await Body(first);
        Assert.Equal(100m, firstBody.GetProperty("balance").GetDecimal());
        Assert.Equal(100m, firstBody.GetProperty("entry").GetProperty("amount").GetDecimal());
        Assert.Equal("adjustment", firstBody.GetProperty("entry").GetProperty("kind").GetString());
        Assert.Equal("Delivery 1", firstBody.GetProperty("entry").GetProperty("note").GetString());
        Assert.False(string.IsNullOrWhiteSpace(firstBody.GetProperty("entry").GetProperty("userName").GetString()));

        var second = await Body(await AddDebt(admin, id, 160m, iOweMore: false)); // pays the 100 off and 60 more: the supplier now owes the shop 60
        Assert.Equal(-160m, second.GetProperty("entry").GetProperty("amount").GetDecimal());
        Assert.Equal(-60m, second.GetProperty("balance").GetDecimal());
        Assert.Equal(-60m, await BalanceAsync(admin, id));

        var ledger = await Body(await admin.GetAsync($"/api/v1/suppliers/{id}/debt-transactions"));
        var amounts = ledger.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("amount").GetDecimal()).ToList();
        Assert.Equal(new[] { -160m, 100m }, amounts); // newest first
        Assert.Equal(amounts.Sum(), await BalanceAsync(admin, id));
    }

    [Fact]
    public async Task Many_small_entries_keep_the_balance_exact()
    {
        var admin = await Admin();
        var (id, _) = await CreateAsync(admin);

        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.Created, (await AddDebt(admin, id, 0.1m, true)).StatusCode);
        await AddDebt(admin, id, 0.35m, false);

        Assert.Equal(0.65m, await BalanceAsync(admin, id));
        var ledger = await Body(await admin.GetAsync($"/api/v1/suppliers/{id}/debt-transactions?pageSize=3&page=2"));
        Assert.Equal(11, ledger.GetProperty("total").GetInt32());
        Assert.Equal(3, ledger.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task An_amount_of_zero_or_less_is_refused_and_the_balance_stays(decimal amount)
    {
        var admin = await Admin();
        var (id, _) = await CreateAsync(admin);

        await AssertProblem(await AddDebt(admin, id, amount, true), HttpStatusCode.BadRequest, "validation_failed");

        Assert.Equal(0m, await BalanceAsync(admin, id));
        Assert.Equal(0, (await Body(await admin.GetAsync($"/api/v1/suppliers/{id}/debt-transactions"))).GetProperty("total").GetInt32());
    }

    // ---- delete ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_supplier_with_a_balance_either_way_cannot_be_deleted()
    {
        await GrantAdminSupplierDeleteAsync();
        var admin = await Admin();
        var (owed, _) = await CreateAsync(admin);
        var (owing, _) = await CreateAsync(admin);
        await AddDebt(admin, owed, 10, true);
        await AddDebt(admin, owing, 10, false);

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/suppliers/{owed}")), HttpStatusCode.Conflict, "supplier_has_balance");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/suppliers/{owing}")), HttpStatusCode.Conflict, "supplier_has_balance");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/suppliers/{owed}")).StatusCode);

        // Settled to zero: the supplier is soft-deleted (its ledger rows stay for history), so the delete goes through.
        await AddDebt(admin, owed, 10, false);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/suppliers/{owed}"))).StatusCode);
    }

    [Fact]
    public async Task A_supplier_with_no_balance_and_no_history_is_deleted()
    {
        await GrantAdminSupplierDeleteAsync();
        var admin = await Admin();
        var (id, _) = await CreateAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/suppliers/{id}"))).StatusCode);

        await AssertProblem(await admin.GetAsync($"/api/v1/suppliers/{id}"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/suppliers/{id}")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_supplier_that_products_point_at_cannot_be_deleted()
    {
        await GrantAdminSupplierDeleteAsync();
        var admin = await Admin();
        var (id, _) = await CreateAsync(admin);
        var lookups = await Body(await admin.GetAsync("/api/v1/products/lookups"));
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/products", new Dictionary<string, object?>
        {
            ["name"] = Unique("Held"),
            ["barcode"] = "H" + Guid.NewGuid().ToString("N")[..12],
            ["categoryId"] = lookups.GetProperty("categories")[0].GetProperty("id").GetInt32(),
            ["supplierId"] = id,
            ["currencyId"] = 0,
            ["purchasePrice"] = 1m,
            ["sellingPrice"] = 2m,
            ["stockQuantity"] = 1m,
            ["isActive"] = true,
        }))).StatusCode);

        var response = await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/suppliers/{id}"));

        await AssertProblem(response, HttpStatusCode.Conflict, "supplier_in_use");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/suppliers/{id}")).StatusCode);
    }

    // ---- export ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_csv_export_lists_the_suppliers()
    {
        var admin = await Admin();
        var (_, name) = await CreateAsync(admin);

        var response = await admin.GetAsync("/api/v1/suppliers/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(name, await response.Content.ReadAsStringAsync());
    }
}
