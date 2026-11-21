using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sync;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.License;

/// <summary>
/// A shop on the Basic plan: the Pro pages answer 403 <c>plan_upgrade_required</c> (and stay in the menu, locked), the pages every plan has
/// keep working, and a fourth active user is refused. The demo database already has four active users (admin, layla, omar, sara), the
/// "after a downgrade" case: they all stay active, only adding one more is refused.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PlanApiTests : CatalogApiTestBase, IDisposable
{
    public PlanApiTests() : base(ApiFactory.Basic())
    {
    }

    public void Dispose() => Factory.Dispose();

    [Fact]
    public async Task Pro_pages_are_locked_and_the_rest_keep_working()
    {
        var admin = await Admin();

        foreach (var (url, feature) in new[]
                 {
                     ("/api/v1/inventory", "Inventory"),
                     ("/api/v1/suppliers", "Suppliers"),
                     ("/api/v1/purchases", "Purchases"),
                     ("/api/v1/audit-logs", "AuditLog"),
                 })
        {
            var response = await admin.SendAsync(Json(HttpMethod.Get, url));
            await AssertProblem(response, HttpStatusCode.Forbidden, "plan_upgrade_required");
            Assert.Equal(feature, (await Body(response)).GetProperty("feature").GetString());
        }

        // Online Whish payments are Pro; taking Whish by hand (the wallet page) is in every plan.
        var online = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/whish/requests",
            new { walletId = 1, amount = 5m, currencyId = 1, idempotencyKey = Guid.NewGuid().ToString("N") }));
        await AssertProblem(online, HttpStatusCode.Forbidden, "plan_upgrade_required");
        Assert.Equal("OnlinePayments", (await Body(online)).GetProperty("feature").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/wallets"))).StatusCode);

        foreach (var url in new[] { "/api/v1/products", "/api/v1/customers", "/api/v1/orders", "/api/v1/users" })
            Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Get, url))).StatusCode);

        var session = await Body(await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/session")));
        Assert.Equal("Basic", session.GetProperty("license").GetProperty("plan").GetString());
        Assert.Equal(3, session.GetProperty("license").GetProperty("maxActiveUsers").GetInt32());

        var nav = session.GetProperty("nav").EnumerateArray().SelectMany(s => s.GetProperty("items").EnumerateArray()).ToList();
        JsonElement Item(string key) => nav.Single(i => i.GetProperty("key").GetString() == key);
        Assert.True(Item("Inventory").GetProperty("locked").GetBoolean());
        Assert.True(Item("AuditLogs").GetProperty("locked").GetBoolean());
        Assert.Equal("Inventory", Item("Inventory").GetProperty("feature").GetString());
        Assert.False(Item("Products").GetProperty("locked").GetBoolean());
        Assert.False(Item("Customers").GetProperty("locked").GetBoolean());
    }

    [Fact]
    public async Task Reports_and_the_dashboard_chart_cover_a_week()
    {
        var admin = await Admin();

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/reports/sales?from=2026-10-01&to=2026-10-07"))).StatusCode);
        foreach (var url in new[] { "/api/v1/reports/sales?from=2026-10-01&to=2026-10-08", "/api/v1/reports/sales.csv?from=2026-10-01&to=2026-10-31" })
        {
            var response = await admin.SendAsync(Json(HttpMethod.Get, url));
            await AssertProblem(response, HttpStatusCode.Forbidden, "plan_upgrade_required");
            Assert.Equal("Reports", (await Body(response)).GetProperty("feature").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/dashboard/sales-trend?period=week"))).StatusCode);
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/dashboard/sales-trend?period=month")), HttpStatusCode.Forbidden, "plan_upgrade_required");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/dashboard/sales-trend")), HttpStatusCode.Forbidden, "plan_upgrade_required");
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/dashboard"))).StatusCode);
    }

    [Fact]
    public async Task The_shop_PC_refuses_a_stock_change_or_a_supplier_sent_from_the_phone_but_takes_a_customer()
    {
        async Task<CommandResult> RunAsync(string type, object payload)
        {
            using var scope = Factory.Services.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IOnlineCommandHandler>().Single(h => h.Type == type);
            return await handler.HandleAsync(JsonSerializer.SerializeToElement(payload, SyncWire.Json), new CommandContext(Guid.NewGuid(), "Layla"), default);
        }

        var stock = await RunAsync("stock.adjust", new { productId = 1, type = "add", quantity = 5 });
        Assert.Equal(CommandResult.Rejected, stock.Outcome);
        Assert.Equal(CommandErrorCodes.PlanUpgradeRequired, stock.Code);
        Assert.Equal("Inventory", stock.Data!.Value.GetProperty("feature").GetString());

        var supplier = await RunAsync("supplier.create", new { name = "Plan Test Co", phone = "01234567", currencyId = 1 });
        Assert.Equal(CommandErrorCodes.PlanUpgradeRequired, supplier.Code);

        var customer = await RunAsync("customer.create", new { firstName = "Nadine", lastName = "Plan", phoneNumber = "71234567" });
        Assert.True(customer.IsApplied, customer.Message);
    }

    [Fact]
    public async Task A_fourth_active_user_is_refused_but_an_inactive_one_can_be_added()
    {
        var admin = await Admin();
        var roles = await Body(await admin.GetAsync("/api/v1/roles?pageSize=200"));
        var cashier = roles.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Cashier").GetProperty("id").GetInt32();

        object NewUser(bool active)
        {
            var name = $"plan{Guid.NewGuid():N}"[..14];
            return new { username = name, email = $"{name}@example.com", firstName = "T", lastName = "U", roleId = cashier, isActive = active, pin = "4321" };
        }

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/users", NewUser(active: true))),
            HttpStatusCode.Conflict, "plan_user_limit");

        var created = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/users", NewUser(active: false)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Body(created)).GetProperty("id").GetInt32();

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/users/{id}/active", new { isActive = true })),
            HttpStatusCode.Conflict, "plan_user_limit");

        // Users who were already active (over the limit after a downgrade) can still sign in and work.
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/users"))).StatusCode);
        await Cashier();
    }
}
