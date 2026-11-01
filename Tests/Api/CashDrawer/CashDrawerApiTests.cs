using System.Net;
using System.Text.Json;
using Application.IServices.Users;
using Core.Entities.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Infrastructure;
using Tests.Api.Pos;
using Web.Helpers;
using Web.Services;
using Xunit;
using static Tests.Api.Pos.PosTestKit;

namespace Tests.Api.CashDrawer;

/// <summary>
/// Cash drawer API (track S2): the permission matrix of the old controller, open / drop / close arithmetic against real sales,
/// the Z report numbers, history ranges and the refusals (already open, closed, unknown).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CashDrawerApiTests
{
    private const string Base = "/api/v1/cash-drawer";
    private const int Usd = 1, Cash = 1;

    private readonly WebApplicationFactory<Program> _host;

    public CashDrawerApiTests(ApiFactory factory) => _host = Selling(factory);

    private async Task<HttpClient> Admin() => (await SignedIn(_host, ApiFactory.AdminUser, ApiFactory.AdminPin)).Browser;

    /// <summary>The admin's browser with no open session: an earlier test of the run may have left one.</summary>
    private async Task<HttpClient> FreshAdmin()
    {
        var admin = await Admin();
        var current = await Body(await admin.GetAsync($"{Base}/current"));
        if (current.GetProperty("status").GetProperty("isOpen").GetBoolean())
        {
            var id = current.GetProperty("session").GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 0 }))).StatusCode);
        }

        return admin;
    }

    /// <summary>A user whose role holds exactly <c>Sales.View</c> (no shipped role holds even that besides Admin).</summary>
    private async Task<HttpClient> Viewer()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        using var scope = _host.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleAccessService>();
        var role = await roles.CreateAsync($"Drawer viewer {tag}", "S2 tests", [PermissionHelper.Permissions.Sales_View]);
        Assert.True(role.Success, role.Error);

        var username = $"dv{tag}";
        await scope.ServiceProvider.GetRequiredService<IUserService>()
            .CreateWithPinAsync(new User(username, $"{username}@example.test", "unused", role.RoleId!.Value, "Dina", "Viewer"), "2468");
        return (await SignedIn(_host, username, "2468")).Browser;
    }

    private static async Task<JsonElement> Open(HttpClient browser, decimal opening, string? notes = null)
    {
        var response = await browser.SendAsync(Json(HttpMethod.Post, $"{Base}/open", new { openingCash = opening, notes }));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Body(response);
    }

    private static async Task<JsonElement> Current(HttpClient browser) => await Body(await browser.GetAsync($"{Base}/current"));

    private static async Task SellOne(HttpClient browser, int productId, int quantity)
    {
        var cart = Cart(Usd, [Line(productId, quantity)]);
        var total = (await Price(browser, cart)).GetProperty("total").GetDecimal();
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/sales/checkout", Checkout(cart, total, Cash, requireOpenDrawer: true)));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static string[] Urls(int id) =>
    [
        $"{Base}/sessions/{id}", $"{Base}/sessions/{id}/z-report", $"{Base}/sessions/{id}/z-report.pdf", $"{Base}/sessions", $"{Base}/current",
    ];

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_every_endpoint_answers_401()
    {
        var anonymous = _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        foreach (var url in Urls(1))
            await AssertProblem(await anonymous.GetAsync(url), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.SendAsync(Json(HttpMethod.Post, $"{Base}/open", new { openingCash = 1 })), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_without_Sales_View_is_refused_everywhere()
    {
        var (cashier, _) = await Cashier(_host);

        foreach (var url in Urls(1))
            await AssertProblem(await cashier.GetAsync(url), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"{Base}/open", new { openingCash = 1 })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/1/close", new { actualCash = 1 })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/1/drops", new { amount = 1 })), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Sales_View_alone_can_open_and_read_but_not_close_or_drop_cash()
    {
        var viewer = await Viewer();

        var opened = await Open(viewer, 40m);
        var id = opened.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Base}/sessions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Base}/sessions")).StatusCode);
        await AssertProblem(await viewer.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 40 })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await viewer.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/drops", new { amount = 5 })), HttpStatusCode.Forbidden, "forbidden");
        Assert.True((await Current(viewer)).GetProperty("status").GetProperty("isOpen").GetBoolean()); // the refused calls changed nothing
    }

    [Fact]
    public async Task A_mutation_without_the_client_header_is_refused()
    {
        var admin = await Admin();

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/open", new { openingCash = 1 }, clientHeader: false));

        await AssertProblem(response, HttpStatusCode.Forbidden, "client_header_required");
    }

    // ---- open --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Opening_starts_a_session_the_status_and_the_shell_payload_both_show()
    {
        var admin = await FreshAdmin();
        var before = await Current(admin);

        var opened = await Open(admin, 100m, "  morning float ");
        var current = await Current(admin);
        var shell = await Body(await admin.GetAsync("/api/v1/session"));

        Assert.False(before.GetProperty("status").GetProperty("isOpen").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before.TryGetProperty("session", out var none) ? none.ValueKind : JsonValueKind.Null);
        Assert.True(opened.GetProperty("isOpen").GetBoolean());
        Assert.Equal(100m, opened.GetProperty("openingCash").GetDecimal());
        Assert.Equal(100m, opened.GetProperty("expectedCash").GetDecimal());
        Assert.Equal("morning float", opened.GetProperty("openingNotes").GetString());
        Assert.False(string.IsNullOrWhiteSpace(opened.GetProperty("cashierName").GetString()));
        Assert.Equal(opened.GetProperty("id").GetInt32(), current.GetProperty("status").GetProperty("sessionId").GetInt32());
        Assert.Equal(100m, current.GetProperty("status").GetProperty("openingCash").GetDecimal());
        Assert.Equal(opened.GetProperty("id").GetInt32(), shell.GetProperty("drawer").GetProperty("sessionId").GetInt32());
    }

    [Fact]
    public async Task A_second_open_is_refused_and_says_so_in_the_users_language()
    {
        var admin = await FreshAdmin();
        await Open(admin, 10m);

        var again = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/open", new { openingCash = 10 }));
        var arabic = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/open", new { openingCash = 10 }, language: "ar"));

        await AssertProblem(again, HttpStatusCode.Conflict, "drawer_already_open");
        Assert.Equal("You already have an open cash drawer session.", (await Body(again)).GetProperty("detail").GetString());
        Assert.Contains("جلسة", (await Body(arabic)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_negative_or_absurd_opening_cash_is_a_validation_error_but_zero_is_fine()
    {
        var admin = await FreshAdmin();

        var negative = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/open", new { openingCash = -1 }));
        var tooLong = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/open", new { openingCash = 1, notes = new string('x', 1001) }));

        await AssertProblem(negative, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(negative)).GetProperty("errors").TryGetProperty("openingCash", out _));
        await AssertProblem(tooLong, HttpStatusCode.BadRequest, "validation_failed");
        Assert.False((await Current(admin)).GetProperty("status").GetProperty("isOpen").GetBoolean());

        var zero = await Open(admin, 0m);
        Assert.Equal(0m, zero.GetProperty("openingCash").GetDecimal());
    }

    // ---- the day: sell, drop, close ---------------------------------------------------------------------------

    [Fact]
    public async Task Expected_cash_is_opening_plus_sales_minus_drops_and_the_close_stores_the_difference()
    {
        var admin = await FreshAdmin();
        var opened = await Open(admin, 100m);
        var id = opened.GetProperty("id").GetInt32();

        await SellOne(admin, 48, 2); // 6.40
        var drop = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/drops", new { amount = 30, reason = "Safe Deposit", notes = "to the safe" }));
        var running = (await Current(admin)).GetProperty("session");

        Assert.Equal(HttpStatusCode.Created, drop.StatusCode);
        Assert.Equal(6.4m, running.GetProperty("totalSales").GetDecimal());
        Assert.Equal(1, running.GetProperty("totalTransactions").GetInt32());
        Assert.Equal(30m, running.GetProperty("totalCashDrops").GetDecimal());
        Assert.Equal(76.4m, running.GetProperty("expectedCash").GetDecimal()); // 100 + 6.40 - 30
        Assert.Equal(JsonValueKind.Null, running.GetProperty("actualCash").ValueKind);
        var drops = running.GetProperty("cashDrops");
        Assert.Equal(1, drops.GetArrayLength());
        Assert.Equal("Safe Deposit", drops[0].GetProperty("reason").GetString());
        Assert.Equal("to the safe", drops[0].GetProperty("notes").GetString());

        var closed = await Body(await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 80, notes = "counted twice" })));

        Assert.False(closed.GetProperty("isOpen").GetBoolean());
        Assert.Equal(76.4m, closed.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(80m, closed.GetProperty("actualCash").GetDecimal());
        Assert.Equal(3.6m, closed.GetProperty("cashDifference").GetDecimal()); // surplus
        Assert.Equal("counted twice", closed.GetProperty("closingNotes").GetString());
        Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("closedAt").ValueKind);
        Assert.False((await Current(admin)).GetProperty("status").GetProperty("isOpen").GetBoolean());
    }

    [Fact]
    public async Task A_shortage_is_negative_and_counting_zero_is_allowed()
    {
        var admin = await FreshAdmin();
        var id = (await Open(admin, 50m)).GetProperty("id").GetInt32();

        var closed = await Body(await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 0 })));

        Assert.Equal(-50m, closed.GetProperty("cashDifference").GetDecimal());
    }

    [Fact]
    public async Task A_cash_drop_needs_a_positive_amount_and_an_open_known_session()
    {
        var admin = await FreshAdmin();
        var id = (await Open(admin, 20m)).GetProperty("id").GetInt32();

        var zero = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/drops", new { amount = 0 }));
        var negative = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/drops", new { amount = -5 }));
        var unknown = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/999999/drops", new { amount = 5 }));
        await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 20 }));
        var afterClose = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/drops", new { amount = 5 }));

        await AssertProblem(zero, HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(negative, HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(unknown, HttpStatusCode.NotFound, "session_not_found");
        await AssertProblem(afterClose, HttpStatusCode.Conflict, "session_closed");
        Assert.Equal(0m, (await Body(await admin.GetAsync($"{Base}/sessions/{id}"))).GetProperty("totalCashDrops").GetDecimal());
    }

    [Fact]
    public async Task Closing_twice_or_closing_an_unknown_session_is_refused()
    {
        var admin = await FreshAdmin();
        var id = (await Open(admin, 20m)).GetProperty("id").GetInt32();
        await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 20 }));

        var twice = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 99 }));
        var unknown = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/999999/close", new { actualCash = 1 }));
        var negative = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = -1 }));

        await AssertProblem(twice, HttpStatusCode.Conflict, "session_closed");
        await AssertProblem(unknown, HttpStatusCode.NotFound, "session_not_found");
        await AssertProblem(negative, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(20m, (await Body(await admin.GetAsync($"{Base}/sessions/{id}"))).GetProperty("actualCash").GetDecimal()); // the second count did not overwrite
    }

    // ---- Z report ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Z_report_repeats_the_closed_sessions_figures()
    {
        var admin = await FreshAdmin();
        var id = (await Open(admin, 100m)).GetProperty("id").GetInt32();
        await SellOne(admin, 48, 2); // 6.40
        await SellOne(admin, 7, 1);  // 6.50 (Arabic Coffee)
        await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/drops", new { amount = 25, reason = "Bank Deposit" }));
        await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/drops", new { amount = 10, reason = "Other" }));
        await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 70 }));

        var report = await Body(await admin.GetAsync($"{Base}/sessions/{id}/z-report"));

        Assert.Equal(id, report.GetProperty("sessionId").GetInt32());
        Assert.Equal(100m, report.GetProperty("openingCash").GetDecimal());
        Assert.Equal(2, report.GetProperty("totalTransactions").GetInt32());
        Assert.Equal(12.9m, report.GetProperty("totalSales").GetDecimal());
        Assert.Equal(0m, report.GetProperty("totalDiscounts").GetDecimal());
        Assert.Equal(12.9m, report.GetProperty("netSales").GetDecimal());
        Assert.Equal(12.9m, report.GetProperty("cashSales").GetDecimal());
        Assert.Equal(0m, report.GetProperty("cardSales").GetDecimal());
        Assert.Equal(35m, report.GetProperty("totalCashDrops").GetDecimal());
        Assert.Equal(2, report.GetProperty("cashDropCount").GetInt32());
        Assert.Equal(77.9m, report.GetProperty("expectedCash").GetDecimal()); // 100 + 12.90 - 35
        Assert.Equal(70m, report.GetProperty("actualCash").GetDecimal());
        Assert.Equal(-7.9m, report.GetProperty("cashDifference").GetDecimal());
        Assert.Equal(new[] { "Bank Deposit", "Other" }, report.GetProperty("cashDrops").EnumerateArray().Select(d => d.GetProperty("reason").GetString()).Order().ToArray()); // the shared report lists them newest first
        var orders = report.GetProperty("orders").EnumerateArray().ToList();
        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.StartsWith("ORD-", o.GetProperty("orderNumber").GetString()));
        Assert.All(orders, o => Assert.Equal("", o.GetProperty("customerName").GetString())); // walk-in: the page words it
        Assert.Equal(12.9m, orders.Sum(o => o.GetProperty("totalAmount").GetDecimal()));
    }

    [Fact]
    public async Task The_Z_report_of_an_open_or_unknown_session_is_refused()
    {
        var admin = await FreshAdmin();
        var id = (await Open(admin, 5m)).GetProperty("id").GetInt32();

        foreach (var url in new[] { $"{Base}/sessions/{id}/z-report", $"{Base}/sessions/{id}/z-report.pdf" })
            await AssertProblem(await admin.GetAsync(url), HttpStatusCode.Conflict, "session_still_open");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/z-report/print")), HttpStatusCode.Conflict, "session_still_open");
        await AssertProblem(await admin.GetAsync($"{Base}/sessions/999999/z-report"), HttpStatusCode.NotFound, "session_not_found");
    }

    [Fact]
    public async Task The_Z_report_prints_or_falls_back_to_the_pdf_link_and_the_pdf_is_a_pdf()
    {
        var admin = await FreshAdmin();
        var id = (await Open(admin, 5m)).GetProperty("id").GetInt32();
        await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 5 }));

        var print = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/z-report/print"));
        var pdf = await admin.GetAsync($"{Base}/sessions/{id}/z-report.pdf");

        Assert.Equal(HttpStatusCode.OK, print.StatusCode); // never a failure because of the printer
        var result = await Body(print);
        Assert.Equal($"{Base}/sessions/{id}/z-report.pdf", result.GetProperty("pdfUrl").GetString());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("message").GetString()));
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync()).Take(4).ToArray()));
    }

    // ---- history -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task History_lists_only_my_sessions_newest_first_and_filters_by_local_day()
    {
        var viewer = await Viewer();
        var first = (await Open(viewer, 10m)).GetProperty("id").GetInt32();
        var other = await FreshAdmin(); // somebody else's session must not appear
        var othersId = (await Open(other, 7m)).GetProperty("id").GetInt32();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var all = await Body(await viewer.GetAsync($"{Base}/sessions"));
        var inRange = await Body(await viewer.GetAsync($"{Base}/sessions?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
        var past = await Body(await viewer.GetAsync($"{Base}/sessions?from=2001-01-01&to=2001-01-31"));
        var paged = await Body(await viewer.GetAsync($"{Base}/sessions?page=2&pageSize=1"));

        Assert.Equal(new[] { first }, all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToArray());
        Assert.DoesNotContain(all.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetInt32() == othersId);
        Assert.Equal(1, inRange.GetProperty("total").GetInt32());
        Assert.Equal(0, past.GetProperty("total").GetInt32());
        Assert.Equal(1, paged.GetProperty("total").GetInt32());
        Assert.Equal(0, paged.GetProperty("items").GetArrayLength());

        var row = all.GetProperty("items")[0];
        Assert.True(row.GetProperty("isOpen").GetBoolean());
        Assert.Equal(10m, row.GetProperty("openingCash").GetDecimal());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("cashDifference").ValueKind);
    }

    [Fact]
    public async Task A_closed_session_in_the_history_carries_its_stored_totals()
    {
        var admin = await FreshAdmin();
        var id = (await Open(admin, 100m)).GetProperty("id").GetInt32();
        await SellOne(admin, 48, 1); // 3.20
        await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/sessions/{id}/close", new { actualCash = 100 }));

        var page = await Body(await admin.GetAsync($"{Base}/sessions?pageSize=200"));
        var row = page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("id").GetInt32() == id);

        Assert.False(row.GetProperty("isOpen").GetBoolean());
        Assert.Equal(3.2m, row.GetProperty("totalSales").GetDecimal());
        Assert.Equal(1, row.GetProperty("totalTransactions").GetInt32());
        Assert.Equal(100m, row.GetProperty("closingCash").GetDecimal());
        Assert.Equal(-3.2m, row.GetProperty("cashDifference").GetDecimal());
    }
}
