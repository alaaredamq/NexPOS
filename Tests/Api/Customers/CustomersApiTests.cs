using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.IServices.Users;
using Core.Entities.Inventory;
using Core.Entities.Sales;
using Core.Enum;
using Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Web.Helpers;
using Web.Services;
using Xunit;

namespace Tests.Api.Customers;

/// <summary>
/// Customers API (track S3): the permission matrix of the old controller, list paging / sorting / search, CRUD, the delete rule
/// (no outstanding balance) and the debt ledger (manual entries, payments, overpayment, sale and reversal entries).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CustomersApiTests : CatalogApiTestBase
{
    public CustomersApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private static async Task<int> CreateAsync(HttpClient client, string first, string last = "Tester", object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["firstName"] = first, ["lastName"] = last };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);

        var response = await client.SendAsync(Json(HttpMethod.Post, "/api/v1/customers", body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("id").GetInt32();
    }

    private static Task<HttpResponseMessage> Debt(HttpClient client, int id, decimal amount, bool owesMore, string? note = null)
        => client.SendAsync(Json(HttpMethod.Post, $"/api/v1/customers/{id}/debt-transactions", new { amount, customerOwesMore = owesMore, note }));

    private static async Task<decimal> BalanceOf(HttpClient client, int id)
        => (await Body(await client.GetAsync($"/api/v1/customers/{id}"))).GetProperty("customer").GetProperty("balance").GetDecimal();

    /// <summary>An order for the customer, straight into the database (the till needs a licence and a drawer; this is about the customer's totals).</summary>
    private async Task<int> SeedOrderAsync(int customerId, decimal total, OrderStatus status = OrderStatus.Delivered, DateTime? at = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

        var cart = new Cart(customerId) { IsActive = false };
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        var order = new Order(customerId, cart.Id, total, (int)status, 1);
        if (at is not null) order.CreatedAt = at.Value;
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>The shipped Admin role does not hold <c>Customers.Delete</c> (like Categories.Delete); the delete tests grant it.</summary>
    private async Task GrantAdminCustomerDeleteAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleAccessService>();
        var admin = (await roles.ListAsync()).Single(r => r.Name == "Admin");
        if (admin.Permissions.Contains(PermissionHelper.Permissions.Customers_Delete)) return;

        var result = await roles.UpdateAsync(admin.Id, admin.Name, admin.Description, admin.Permissions.Append(PermissionHelper.Permissions.Customers_Delete));
        Assert.True(result.Success, result.Error);
    }

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_the_list_answers_401()
    {
        var response = await Factory.CreateBrowser().GetAsync("/api/v1/customers");

        await AssertProblem(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_can_read_and_add_customers_but_not_edit_delete_or_adjust_debt()
    {
        var cashier = await Cashier();
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Perm"));

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/customers/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync($"/api/v1/customers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync($"/api/v1/customers/{id}/debt-transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/customers/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/customers", new { firstName = "Walk", lastName = Unique("In") }))).StatusCode);

        var edit = new { firstName = "Nope", lastName = "Nope" };
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Put, $"/api/v1/customers/{id}", edit)), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Delete, $"/api/v1/customers/{id}")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await Debt(cashier, id, 5m, true), HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(0m, await BalanceOf(admin, id));
    }

    [Fact]
    public async Task A_mutation_without_the_client_header_is_refused()
    {
        var admin = await Admin();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers") { Content = JsonContent.Create(new { firstName = "Head", lastName = "Er" }) };

        await AssertProblem(await admin.SendAsync(request), HttpStatusCode.Forbidden, "client_header_required");
    }

    // ---- list --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_pages_sorts_and_searches_by_name_email_and_phone()
    {
        var admin = await Admin();
        var tag = Unique("Srch");
        await CreateAsync(admin, $"{tag}Bravo", "Smith", new { email = $"{tag}@example.com", phoneNumber = "70123456" });
        await CreateAsync(admin, $"{tag}Alpha", "Smith");
        await CreateAsync(admin, $"{tag}Charlie", "Jones");

        var byName = await Body(await admin.GetAsync($"/api/v1/customers?q={tag}&sort=name&dir=asc"));
        Assert.Equal(3, byName.GetProperty("total").GetInt32());
        Assert.Equal(new[] { $"{tag}Alpha", $"{tag}Bravo", $"{tag}Charlie" }, byName.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("firstName").GetString()).ToArray());

        var descending = await Body(await admin.GetAsync($"/api/v1/customers?q={tag}&sort=name&dir=desc&page=2&pageSize=2"));
        Assert.Equal(3, descending.GetProperty("total").GetInt32());
        Assert.Equal(new[] { $"{tag}Alpha" }, descending.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("firstName").GetString()).ToArray());

        // Email and phone are searched too; the full name ("first last") matches as one phrase.
        Assert.Equal(1, (await Body(await admin.GetAsync("/api/v1/customers?q=70123456"))).GetProperty("items").EnumerateArray().Count(c => c.GetProperty("firstName").GetString() == $"{tag}Bravo"));
        Assert.Equal(1, (await Body(await admin.GetAsync($"/api/v1/customers?q={tag}%40example"))).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Body(await admin.GetAsync($"/api/v1/customers?q={tag}Charlie%20Jones"))).GetProperty("total").GetInt32());

        // An unknown sort column falls back to the name instead of failing.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/customers?q={tag}&sort=passwordHash")).StatusCode);
    }

    [Fact]
    public async Task The_list_counts_purchases_and_leaves_out_cancelled_and_returned_orders()
    {
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Buy"));
        await SeedOrderAsync(id, 10m);
        await SeedOrderAsync(id, 5.5m, OrderStatus.Delivered);
        await SeedOrderAsync(id, 99m, OrderStatus.Cancelled);
        await SeedOrderAsync(id, 77m, OrderStatus.Returned);

        var row = (await Body(await admin.GetAsync($"/api/v1/customers?q={Uri.EscapeDataString("Tester")}&pageSize=200"))).GetProperty("items").EnumerateArray().Single(c => c.GetProperty("id").GetInt32() == id);

        Assert.Equal(2, row.GetProperty("ordersCount").GetInt32());
        Assert.Equal(15.5m, row.GetProperty("totalSpent").GetDecimal());
    }

    [Fact]
    public async Task The_list_sorts_by_total_spent_and_by_balance()
    {
        var admin = await Admin();
        var tag = Unique("Rank");
        var small = await CreateAsync(admin, $"{tag}A");
        var big = await CreateAsync(admin, $"{tag}B");
        await SeedOrderAsync(small, 1m);
        await SeedOrderAsync(big, 500m);
        Assert.Equal(HttpStatusCode.Created, (await Debt(admin, small, 40m, true)).StatusCode);

        var spent = await Body(await admin.GetAsync($"/api/v1/customers?q={tag}&sort=totalSpent&dir=desc"));
        Assert.Equal(new[] { big, small }, spent.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToArray());

        var balance = await Body(await admin.GetAsync($"/api/v1/customers?q={tag}&sort=balance&dir=desc"));
        Assert.Equal(new[] { small, big }, balance.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToArray());
    }

    [Fact]
    public async Task The_summary_counts_customers_and_what_they_owe()
    {
        var admin = await Admin();
        var before = await Body(await admin.GetAsync("/api/v1/customers/summary"));
        var id = await CreateAsync(admin, Unique("Sum"));
        Assert.Equal(HttpStatusCode.Created, (await Debt(admin, id, 12.5m, true)).StatusCode);

        var after = await Body(await admin.GetAsync("/api/v1/customers/summary"));

        Assert.Equal(before.GetProperty("total").GetInt32() + 1, after.GetProperty("total").GetInt32());
        Assert.Equal(before.GetProperty("owing").GetInt32() + 1, after.GetProperty("owing").GetInt32());
        Assert.Equal(before.GetProperty("totalOwed").GetDecimal() + 12.5m, after.GetProperty("totalOwed").GetDecimal());
        Assert.False(string.IsNullOrEmpty(after.GetProperty("currencyNotation").GetString()));
    }

    // ---- create / update ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_trims_the_text_and_stores_no_email_as_null()
    {
        var admin = await Admin();
        var tag = Unique("Trim");

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/customers", new { firstName = $"  {tag}  ", lastName = "  Lee ", phoneNumber = " 71111111 ", address = "   " }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Body(response);
        Assert.Equal(tag, body.GetProperty("firstName").GetString());
        Assert.Equal("Lee", body.GetProperty("lastName").GetString());
        Assert.Equal("71111111", body.GetProperty("phoneNumber").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("address").ValueKind);
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(0m, body.GetProperty("balance").GetDecimal());
        Assert.EndsWith($"/api/v1/customers/{body.GetProperty("id").GetInt32()}", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Create_refuses_short_names_and_bad_email_with_field_errors()
    {
        var admin = await Admin();

        var shortName = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/customers", new { firstName = "A", lastName = "Okay" }));
        await AssertProblem(shortName, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(shortName)).GetProperty("errors").TryGetProperty("firstName", out _));

        // "  x " passes the DTO length check (4 characters) but is one letter once trimmed.
        var padded = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/customers", new { firstName = "Okay", lastName = "  x " }));
        await AssertProblem(padded, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(padded)).GetProperty("errors").TryGetProperty("lastName", out _));

        var badEmail = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/customers", new { firstName = "Okay", lastName = "Okay", email = "not-an-email" }));
        await AssertProblem(badEmail, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(badEmail)).GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Update_changes_the_contact_details_and_keeps_the_balance_and_the_address_extras()
    {
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Edit"), extra: new { address = "Old street" });
        Assert.Equal(HttpStatusCode.Created, (await Debt(admin, id, 30m, true)).StatusCode);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var row = db.Set<Core.Entities.Users.Customer>().Single(c => c.Id == id);
            row.City = "Beirut";
            row.Country = "Lebanon";
            row.PostalCode = "1100";
            await db.SaveChangesAsync();
        }

        var update = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/customers/{id}", new { firstName = "Renamed", lastName = "Person", email = "r@example.com", phoneNumber = "03123456", address = "New street 5", isActive = false }));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var body = await Body(update);
        Assert.Equal("Renamed", body.GetProperty("firstName").GetString());
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(30m, body.GetProperty("balance").GetDecimal());

        var details = await Body(await admin.GetAsync($"/api/v1/customers/{id}"));
        Assert.Equal("New street 5", details.GetProperty("customer").GetProperty("address").GetString());
        Assert.Equal("Beirut", details.GetProperty("city").GetString());
        Assert.Equal("Lebanon", details.GetProperty("country").GetString());
        Assert.Equal("1100", details.GetProperty("postalCode").GetString());
    }

    [Fact]
    public async Task Update_and_get_of_an_unknown_customer_answer_404()
    {
        var admin = await Admin();

        await AssertProblem(await admin.GetAsync("/api/v1/customers/999999"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Put, "/api/v1/customers/999999", new { firstName = "No", lastName = "Body" })), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.GetAsync("/api/v1/customers/999999/debt-transactions"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await Debt(admin, 999999, 1m, true), HttpStatusCode.NotFound, "not_found");
    }

    // ---- details -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Details_show_the_latest_ten_orders_newest_first_and_the_purchase_figures()
    {
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Det"));
        var start = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        var ids = new List<int>();
        for (var i = 0; i < 12; i++) ids.Add(await SeedOrderAsync(id, 10m, OrderStatus.Delivered, start.AddDays(i)));
        var cancelled = await SeedOrderAsync(id, 1000m, OrderStatus.Cancelled, start.AddDays(20));

        var details = await Body(await admin.GetAsync($"/api/v1/customers/{id}"));

        var recent = details.GetProperty("recentOrders").EnumerateArray().ToList();
        Assert.Equal(10, recent.Count);
        Assert.Equal(cancelled, recent[0].GetProperty("id").GetInt32());
        Assert.Equal("Cancelled", recent[0].GetProperty("status").GetString());
        Assert.Equal(ids[11], recent[1].GetProperty("id").GetInt32());
        Assert.Equal("Delivered", recent[1].GetProperty("status").GetString());
        Assert.Equal(13, details.GetProperty("allOrdersCount").GetInt32());
        Assert.Equal(12, details.GetProperty("customer").GetProperty("ordersCount").GetInt32());
        Assert.Equal(120m, details.GetProperty("customer").GetProperty("totalSpent").GetDecimal());
        Assert.Equal(10m, details.GetProperty("averageOrder").GetDecimal());
        Assert.Equal(start.AddDays(20), details.GetProperty("lastOrderAt").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task Details_of_a_customer_without_orders_have_zeroes_and_no_last_order()
    {
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Nobuy"));

        var details = await Body(await admin.GetAsync($"/api/v1/customers/{id}"));

        Assert.Equal(0, details.GetProperty("allOrdersCount").GetInt32());
        Assert.Equal(0m, details.GetProperty("averageOrder").GetDecimal());
        Assert.Equal(JsonValueKind.Null, details.GetProperty("lastOrderAt").ValueKind);
        Assert.Empty(details.GetProperty("recentOrders").EnumerateArray());
    }

    // ---- debt ledger -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_debt_entry_moves_the_balance_and_is_listed_newest_first_with_its_author()
    {
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Debt"));

        var up = await Debt(admin, id, 100m, true, "  Credit sale  ");
        Assert.Equal(HttpStatusCode.Created, up.StatusCode);
        var upBody = await Body(up);
        Assert.Equal(100m, upBody.GetProperty("balance").GetDecimal());
        Assert.Equal(100m, upBody.GetProperty("transaction").GetProperty("amount").GetDecimal());
        Assert.Equal("ManualAdjustment", upBody.GetProperty("transaction").GetProperty("type").GetString());
        Assert.Equal("Credit sale", upBody.GetProperty("transaction").GetProperty("note").GetString());
        Assert.Equal(ApiFactory.AdminUser, upBody.GetProperty("transaction").GetProperty("recordedBy").GetString());

        var down = await Body(await Debt(admin, id, 30.25m, false));
        Assert.Equal(69.75m, down.GetProperty("balance").GetDecimal());
        Assert.Equal(-30.25m, down.GetProperty("transaction").GetProperty("amount").GetDecimal());
        Assert.Equal(69.75m, await BalanceOf(admin, id));

        var ledger = await Body(await admin.GetAsync($"/api/v1/customers/{id}/debt-transactions"));
        Assert.Equal(2, ledger.GetProperty("total").GetInt32());
        Assert.Equal(new[] { -30.25m, 100m }, ledger.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("amount").GetDecimal()).ToArray());
    }

    [Fact]
    public async Task Paying_more_than_is_owed_leaves_a_negative_balance_the_business_owes_the_customer()
    {
        // Overpayment rule, kept as the Razor page had it: no cap. The customer is simply in credit afterwards.
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Over"));
        await Debt(admin, id, 20m, true);

        var paid = await Body(await Debt(admin, id, 50m, false, "Overpaid"));

        Assert.Equal(-30m, paid.GetProperty("balance").GetDecimal());
        Assert.Equal(-30m, await BalanceOf(admin, id));
        var summary = await Body(await admin.GetAsync("/api/v1/customers/summary"));
        Assert.True(summary.GetProperty("totalOwed").GetDecimal() >= 0m); // credit is not counted as a debt
    }

    [Fact]
    public async Task A_payment_of_the_exact_balance_settles_it_to_zero()
    {
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Settle"));
        await Debt(admin, id, 12.34m, true);

        var settled = await Body(await Debt(admin, id, 12.34m, false));

        Assert.Equal(0m, settled.GetProperty("balance").GetDecimal());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.001)]
    public async Task A_zero_or_negative_amount_is_refused_and_the_balance_does_not_move(double amount)
    {
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Zero"));

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/customers/{id}/debt-transactions", new { amount, customerOwesMore = true }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(response)).GetProperty("errors").TryGetProperty("amount", out _));
        Assert.Equal(0m, await BalanceOf(admin, id));
    }

    [Fact]
    public async Task The_ledger_pages_and_also_shows_sales_on_account_and_their_reversal()
    {
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Ledg"));
        var order = await SeedOrderAsync(id, 40m);

        // What the till and the void-receipt flow do (the POS checkout test covers the HTTP side): a sale entry, then its cancelling reversal.
        using (var scope = Factory.Services.CreateScope())
        {
            var debts = scope.ServiceProvider.GetRequiredService<ICustomerDebtService>();
            await debts.AddSaleTransactionAsync(id, 1, order, 40m);
            Assert.Equal(40m, (await debts.ReverseSaleAsync(order, 1, "Receipt voided")));
            Assert.Equal(0m, await debts.ReverseSaleAsync(order, 1)); // a second void adds nothing
        }

        Assert.Equal(0m, await BalanceOf(admin, id));
        await Debt(admin, id, 5m, true);
        var page1 = await Body(await admin.GetAsync($"/api/v1/customers/{id}/debt-transactions?pageSize=2&page=1"));
        var page2 = await Body(await admin.GetAsync($"/api/v1/customers/{id}/debt-transactions?pageSize=2&page=2"));
        Assert.Equal(3, page1.GetProperty("total").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());

        var all = (await Body(await admin.GetAsync($"/api/v1/customers/{id}/debt-transactions"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "ManualAdjustment", "SaleReversal", "Sale" }, all.Select(t => t.GetProperty("type").GetString()).ToArray());
        Assert.Equal(order, all[1].GetProperty("orderId").GetInt32());
        Assert.Equal("Receipt voided", all[1].GetProperty("note").GetString());
    }

    // ---- delete ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_is_refused_while_the_balance_is_not_zero_in_either_direction_and_allowed_once_settled()
    {
        await GrantAdminCustomerDeleteAsync();
        var admin = await Admin();
        var owing = await CreateAsync(admin, Unique("Owes"));
        var inCredit = await CreateAsync(admin, Unique("Credit"));
        await Debt(admin, owing, 15m, true);
        await Debt(admin, inCredit, 15m, false);

        var owingResponse = await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/customers/{owing}"));
        await AssertProblem(owingResponse, HttpStatusCode.Conflict, "customer_has_balance");
        Assert.Contains("15.00", (await Body(owingResponse)).GetProperty("detail").GetString());
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/customers/{inCredit}")), HttpStatusCode.Conflict, "customer_has_balance");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/customers/{owing}")).StatusCode);

        await Debt(admin, owing, 15m, false);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/customers/{owing}"))).StatusCode);
        await AssertProblem(await admin.GetAsync($"/api/v1/customers/{owing}"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/customers/{owing}")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_customer_with_order_history_can_still_be_deleted_once_settled_and_leaves_the_list()
    {
        await GrantAdminCustomerDeleteAsync();
        var admin = await Admin();
        var tag = Unique("Hist");
        var id = await CreateAsync(admin, tag);
        await SeedOrderAsync(id, 9m);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/customers/{id}"))).StatusCode);

        Assert.Equal(0, (await Body(await admin.GetAsync($"/api/v1/customers?q={tag}"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Without_Customers_Delete_the_delete_is_a_403()
    {
        // No default role holds Customers.Delete (not even Admin), so on a fresh install the button is a 403 until a role is edited.
        var admin = await Admin();
        var id = await CreateAsync(admin, Unique("Keep"));
        using (var scope = Factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleAccessService>();
            var role = (await roles.ListAsync()).Single(r => r.Name == "Admin");
            if (role.Permissions.Contains(PermissionHelper.Permissions.Customers_Delete))
                return; // another test of this run granted it already: nothing left to prove

            await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/customers/{id}")), HttpStatusCode.Forbidden, "forbidden");
        }
    }

    // ---- export ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Export_returns_a_csv_file_with_the_customers()
    {
        var admin = await Admin();
        var tag = Unique("Csv");
        await CreateAsync(admin, tag);

        var response = await admin.GetAsync("/api/v1/customers/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches(@"^customers_export_\d{8}_\d{6}\.csv$", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains(tag, await response.Content.ReadAsStringAsync());
    }
}
