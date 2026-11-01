using System.Net.Http.Json;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Infrastructure;
using Web.Helpers;
using Web.Services;
using Xunit;

namespace Tests.Api.Catalog;

/// <summary>Categories API (track C2): the permission matrix of the old controller, list paging / sorting / search, CRUD and the "still used by products" rule.</summary>
[Collection(ApiCollection.Name)]
public sealed class CategoriesApiTests : CatalogApiTestBase
{
    public CategoriesApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static async Task<int> CreateAsync(HttpClient admin, string name, string? description = null, bool isActive = true)
    {
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/categories", new { name, description, isActive }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("id").GetInt32();
    }

    /// <summary>
    /// The shipped Admin role does not hold <c>Categories.Delete</c> (no default role does, so the Razor delete button is a 403 until the Users page grants it).
    /// The delete tests grant it, which also proves a role edit reaches an already-open session on its next request.
    /// </summary>
    private async Task GrantAdminCategoryDeleteAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleAccessService>();
        var admin = (await roles.ListAsync()).Single(r => r.Name == "Admin");
        if (admin.Permissions.Contains(PermissionHelper.Permissions.Categories_Delete)) return;

        var result = await roles.UpdateAsync(admin.Id, admin.Name, admin.Description, admin.Permissions.Append(PermissionHelper.Permissions.Categories_Delete));
        Assert.True(result.Success, result.Error);
    }

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_the_list_answers_401()
    {
        var response = await Factory.CreateBrowser().GetAsync("/api/v1/categories");

        await AssertProblem(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_can_read_categories_but_not_change_them()
    {
        var cashier = await Cashier();

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/categories/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/categories/export")).StatusCode);

        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/categories", new { name = "Nope" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Put, "/api/v1/categories/1", new { name = "Nope" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Delete, "/api/v1/categories/1")), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_mutation_without_the_client_header_is_refused()
    {
        var admin = await Admin();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/categories") { Content = JsonContent.Create(new { name = "Header" }) };

        await AssertProblem(await admin.SendAsync(request), HttpStatusCode.Forbidden, "client_header_required");
    }

    // ---- list --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_pages_sorts_and_searches()
    {
        var admin = await Admin();
        var tag = Unique("Zlist");
        var idB = await CreateAsync(admin, $"{tag}-B", "second");
        var idA = await CreateAsync(admin, $"{tag}-A", "first");

        var search = await Body(await admin.GetAsync($"/api/v1/categories?q={tag}&sort=name&dir=asc"));
        var names = search.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { $"{tag}-A", $"{tag}-B" }, names);
        Assert.Equal(2, search.GetProperty("total").GetInt32());

        var desc = await Body(await admin.GetAsync($"/api/v1/categories?q={tag}&sort=name&dir=desc&pageSize=1&page=2"));
        Assert.Equal(2, desc.GetProperty("total").GetInt32());
        Assert.Equal($"{tag}-A", desc.GetProperty("items")[0].GetProperty("name").GetString());

        var byDescription = await Body(await admin.GetAsync($"/api/v1/categories?q=second&sort=description"));
        Assert.Contains(byDescription.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetInt32() == idB);
        Assert.DoesNotContain(byDescription.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetInt32() == idA);
    }

    [Fact]
    public async Task An_unknown_sort_column_falls_back_to_name_instead_of_failing()
    {
        var admin = await Admin();

        var response = await admin.GetAsync("/api/v1/categories?sort=password");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_list_counts_products_and_the_summary_adds_up()
    {
        var admin = await Admin();
        var list = await Body(await admin.GetAsync("/api/v1/categories?pageSize=200"));
        var items = list.GetProperty("items").EnumerateArray().ToList();
        var summary = await Body(await admin.GetAsync("/api/v1/categories/summary"));

        Assert.Equal(items.Count, summary.GetProperty("total").GetInt32());
        Assert.Equal(items.Count, summary.GetProperty("active").GetInt32() + summary.GetProperty("inactive").GetInt32());
        Assert.Equal(items.Sum(i => i.GetProperty("productCount").GetInt32()), summary.GetProperty("products").GetInt32());
        Assert.True(summary.GetProperty("products").GetInt32() > 0, "the demo catalogue has products");
    }

    // ---- CRUD --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_trims_and_stores_the_category_and_it_has_no_colour_or_image()
    {
        var admin = await Admin();

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/categories",
            new { name = "  Fresh Bread  ", description = "   ", isActive = false, color = "#ff0000", imageUrl = "http://x/y.png" }));
        var created = await Body(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Fresh Bread", created.GetProperty("name").GetString());
        Assert.False(created.TryGetProperty("description", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String);
        Assert.False(created.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, created.GetProperty("productCount").GetInt32());
        Assert.False(created.TryGetProperty("color", out _));
        Assert.False(created.TryGetProperty("imageUrl", out _));
        Assert.EndsWith($"/api/v1/categories/{created.GetProperty("id").GetInt32()}", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    public async Task Create_rejects_a_name_that_is_missing_or_too_short_with_a_field_error(string name)
    {
        var admin = await Admin();

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/categories", new { name }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(response)).GetProperty("errors").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Update_changes_the_fields_and_an_unknown_id_is_404()
    {
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Upd"), "before");

        var updated = await Body(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/categories/{id}", new { name = "Renamed Cat", description = "after", isActive = false })));

        Assert.Equal("Renamed Cat", updated.GetProperty("name").GetString());
        Assert.Equal("after", updated.GetProperty("description").GetString());
        Assert.False(updated.GetProperty("isActive").GetBoolean());

        var details = await Body(await admin.GetAsync($"/api/v1/categories/{id}"));
        Assert.Equal("Renamed Cat", details.GetProperty("category").GetProperty("name").GetString());

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Put, "/api/v1/categories/999999", new { name = "Ghost" })), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task An_unused_category_can_be_deleted_and_then_is_gone()
    {
        var admin = await Admin();
        await GrantAdminCategoryDeleteAsync();
        var id = await CreateAsync(admin, Unique("Del"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/categories/{id}"))).StatusCode);
        await AssertProblem(await admin.GetAsync($"/api/v1/categories/{id}"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/categories/{id}")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_category_with_products_cannot_be_deleted()
    {
        var admin = await Admin();
        await GrantAdminCategoryDeleteAsync();
        var withProducts = (await Body(await admin.GetAsync("/api/v1/categories?pageSize=200&sort=productCount&dir=desc")))
            .GetProperty("items")[0];
        Assert.True(withProducts.GetProperty("productCount").GetInt32() > 0);
        var id = withProducts.GetProperty("id").GetInt32();

        var response = await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/categories/{id}"));

        await AssertProblem(response, HttpStatusCode.Conflict, "category_in_use");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/categories/{id}")).StatusCode);
    }

    // ---- details / export --------------------------------------------------------------------------------------

    [Fact]
    public async Task Details_carry_the_products_and_the_stock_figures_of_the_category()
    {
        var admin = await Admin();
        var withProducts = (await Body(await admin.GetAsync("/api/v1/categories?pageSize=200&sort=productCount&dir=desc"))).GetProperty("items")[0];
        var id = withProducts.GetProperty("id").GetInt32();

        var details = await Body(await admin.GetAsync($"/api/v1/categories/{id}"));
        var products = details.GetProperty("products").EnumerateArray().ToList();

        Assert.Equal(withProducts.GetProperty("productCount").GetInt32(), details.GetProperty("category").GetProperty("productCount").GetInt32());
        Assert.InRange(products.Count, 1, 20);
        Assert.True(details.GetProperty("totalStock").GetDecimal() >= products.Sum(p => p.GetProperty("stockQuantity").GetDecimal()) || products.Count == 20);
        Assert.False(string.IsNullOrWhiteSpace(details.GetProperty("currencyNotation").GetString()));
        Assert.All(products, p => Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("barcode").GetString())));
    }

    [Fact]
    public async Task Export_downloads_a_csv_file()
    {
        var admin = await Admin();

        var response = await admin.GetAsync("/api/v1/categories/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("categories_export_", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("Name", await response.Content.ReadAsStringAsync());
    }
}
