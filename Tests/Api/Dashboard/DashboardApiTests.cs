using System.Net;
using System.Text.Json;
using Core.ICache;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Infrastructure;
using Xunit;
using static Tests.Api.Pos.PosTestKit;
using static Tests.Api.Reports.InsightsTestKit;

namespace Tests.Api.Dashboard;

/// <summary>
/// Track I1 (D10): the Dashboard API through the real pipeline. The figures are cached for two minutes (as on the Razor page), so a test that
/// sells clears the cache before it looks; the point is that today's tile, the month tile, the latest-orders list and the chart all move by
/// the sale the till just made, and that a cancelled sale leaves them.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DashboardApiTests
{
    private readonly ApiFactory _factory;

    public DashboardApiTests(ApiFactory factory) => _factory = factory;

    private Task<(HttpClient Browser, int UserId)> Admin() => SignedIn(Host(_factory), ApiFactory.AdminUser, ApiFactory.AdminPin);

    /// <summary>The dashboard figures are cached; forget them so the next read is computed from the database.</summary>
    private void ForgetCache()
    {
        using var scope = Host(_factory).Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        // Remove, not Clear(): Clear() only drops what the same (scoped) CacheService instance stored.
        foreach (var key in new[] { CacheKeys.Dashboard("stats"), CacheKeys.Dashboard("recent-orders", "10"), CacheKeys.Dashboard("category-analytics") }.Concat(new[] { "week", "month", "quarter", "year" }.Select(p => CacheKeys.Dashboard("sales-analytics", p))))
            cache.Remove(key);
    }

    private async Task<JsonElement> Dashboard(HttpClient browser)
    {
        ForgetCache();
        return await Ok(await browser.GetAsync("/api/v1/dashboard"));
    }

    private async Task<JsonElement> Trend(HttpClient browser, string period)
    {
        ForgetCache();
        return await Ok(await browser.GetAsync($"/api/v1/dashboard/sales-trend?period={period}"));
    }

    [Fact]
    public async Task Anonymous_callers_are_turned_away()
    {
        var anonymous = _factory.CreateBrowser();

        foreach (var url in new[] { "/api/v1/dashboard", "/api/v1/dashboard/sales-trend" })
            await AssertProblem(await anonymous.GetAsync(url), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_may_see_the_dashboard_as_the_old_page_allowed()
    {
        var (cashier, _) = await Cashier(Host(_factory));

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/dashboard/sales-trend")).StatusCode);
    }

    [Fact]
    public async Task The_payload_has_the_shape_the_page_relies_on()
    {
        var (admin, _) = await Admin();
        await Sell(admin);

        var dashboard = await Dashboard(admin);

        Assert.False(string.IsNullOrWhiteSpace(dashboard.GetProperty("currencyNotation").GetString()));
        Assert.True(dashboard.GetProperty("customerCount").GetInt32() >= 0);
        Assert.True(dashboard.GetProperty("productCount").GetInt32() > 0);

        var orders = dashboard.GetProperty("recentOrders").EnumerateArray().ToList();
        Assert.InRange(orders.Count, 1, 10);
        Assert.Equal(orders.OrderByDescending(o => o.GetProperty("orderDate").GetDateTime()).Select(o => o.GetProperty("id").GetInt32()), orders.Select(o => o.GetProperty("id").GetInt32()));
        Assert.All(orders, o =>
        {
            Assert.InRange(o.GetProperty("status").GetInt32(), 0, 5);
            Assert.EndsWith("Z", o.GetProperty("orderDate").GetString());
        });

        var categories = dashboard.GetProperty("categories").EnumerateArray().ToList();
        Assert.True(categories.Count <= 10);
        Assert.All(categories, c => Assert.True(c.GetProperty("sales").GetDecimal() > 0));
        Assert.Equal(categories.OrderByDescending(c => c.GetProperty("sales").GetDecimal()).Select(c => c.GetProperty("name").GetString()), categories.Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_sale_shows_in_today_the_month_the_latest_orders_and_the_chart_and_a_cancelled_sale_leaves()
    {
        var (admin, _) = await Admin();
        var before = await Dashboard(admin);
        var trendBefore = await Trend(admin, "week");

        var sale = await Sell(admin, quantity: 2);

        var after = await Dashboard(admin);
        Assert.Equal(before.GetProperty("todaySales").GetDecimal() + sale.Total, after.GetProperty("todaySales").GetDecimal());
        Assert.Equal(before.GetProperty("todayOrders").GetInt32() + 1, after.GetProperty("todayOrders").GetInt32());
        Assert.Equal(before.GetProperty("monthSales").GetDecimal() + sale.Total, after.GetProperty("monthSales").GetDecimal());
        Assert.Equal(before.GetProperty("monthOrders").GetInt32() + 1, after.GetProperty("monthOrders").GetInt32());

        var latest = after.GetProperty("recentOrders")[0];
        Assert.Equal(sale.OrderId, latest.GetProperty("id").GetInt32());
        Assert.Equal(sale.Total, latest.GetProperty("amount").GetDecimal());
        Assert.Equal(3, latest.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(latest.GetProperty("paymentMethod").GetString()));
        Assert.Equal(JsonValueKind.Null, latest.GetProperty("customerName").ValueKind);

        var trend = await Trend(admin, "week");
        Assert.Equal(sale.Total, Sales(trend, Today) - Sales(trendBefore, Today));

        var cancel = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/orders/{sale.OrderId}/status", new { status = 4 }));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var cancelled = await Dashboard(admin);
        Assert.Equal(before.GetProperty("todaySales").GetDecimal(), cancelled.GetProperty("todaySales").GetDecimal());
        Assert.Equal(before.GetProperty("monthOrders").GetInt32(), cancelled.GetProperty("monthOrders").GetInt32());
        // The cancelled order is still among the latest orders - with its real status, which the old page painted "Completed".
        var row = cancelled.GetProperty("recentOrders").EnumerateArray().Single(o => o.GetProperty("id").GetInt32() == sale.OrderId);
        Assert.Equal(4, row.GetProperty("status").GetInt32());
        Assert.Equal(Sales(trendBefore, Today), Sales(await Trend(admin, "week"), Today));
    }

    private static decimal Sales(JsonElement trend, string date)
        => trend.GetProperty("points").EnumerateArray().Where(p => p.GetProperty("date").GetString() == date).Sum(p => p.GetProperty("sales").GetDecimal());

    [Theory]
    [InlineData("week", "week")]
    [InlineData("month", "month")]
    [InlineData("quarter", "quarter")]
    [InlineData("year", "year")]
    [InlineData("QUARTER", "quarter")]
    [InlineData("decade", "month")]
    [InlineData("", "month")]
    public async Task The_chart_answers_every_period_and_an_unknown_one_means_the_month(string asked, string expected)
    {
        var (admin, _) = await Admin();
        await Sell(admin);

        var trend = await Trend(admin, Uri.EscapeDataString(asked));

        Assert.Equal(expected, trend.GetProperty("period").GetString());
        Assert.False(string.IsNullOrWhiteSpace(trend.GetProperty("currencyNotation").GetString()));
        var dates = trend.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("date").GetString()!).ToList();
        Assert.NotEmpty(dates);
        Assert.All(dates, d => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", d));
        Assert.Equal(dates.Order(StringComparer.Ordinal), dates);
        Assert.Equal(dates.Distinct(), dates);
        // Quarter and year are monthly buckets: every date is the 1st.
        if (expected is "quarter" or "year") Assert.All(dates, d => Assert.EndsWith("-01", d));
    }
}
