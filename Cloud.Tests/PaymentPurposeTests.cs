using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// What a payment was for: one or several of the purposes (a licence, a year of Online and the installation in one payment), with the
/// online menu as its own purpose so the menu-only clients' income shows apart.
/// </summary>
public class PaymentPurposeTests
{
    private static async Task<HttpClient> PortalAsync(CloudFactory cloud)
    {
        var c = cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await HtmlForms.PostAsync(c, "/admin/Account/Login", "/admin/Account/Login",
            ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        return c;
    }

    [Fact]
    public void Several_purposes_read_the_same_whatever_order_they_were_ticked_in()
    {
        Assert.Equal("Online subscription + POS licence + Set-up / installation",
            FinanceRules.JoinPurposes(["Set-up / installation", "pos licence", "Online subscription", "POS licence"]));
        Assert.Equal("Online menu + Printer paper", FinanceRules.JoinPurposes(["Printer paper", "Online menu", " "]));
        Assert.Null(FinanceRules.JoinPurposes(["", null]));
        Assert.Equal(["POS licence", "Online subscription"], FinanceRules.PurposesOf("POS licence + Online subscription"));
        Assert.Equal(["Support"], FinanceRules.PurposesOf("Support"));
        Assert.Empty(FinanceRules.PurposesOf(null));
    }

    [Fact]
    public async Task One_payment_covers_a_licence_Online_and_the_installation()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Full Package Shop");
        var c = await PortalAsync(cloud);
        var page = $"/admin/Clients/Details/{seeded.ClientId}";

        // A POS + Online client: Online subscription is ticked to start with, and every purpose is offered.
        var html = await c.GetStringAsync(page);
        Assert.Contains("value=\"Online subscription\" checked=\"checked\"", html);
        Assert.Contains("value=\"Online menu\"", html);
        Assert.Contains("value=\"Set-up / installation\"", html);

        var paid = await HtmlForms.PostAsync(c, page, page + "?handler=Payment",
            ("Pay.Amount", "650"), ("Pay.Date", "2026-11-03"), ("Pay.Method", "Cash"), ("Pay.Purpose", ""),
            ("Pay.Purposes", "Set-up / installation"), ("Pay.Purposes", "POS licence"), ("Pay.Purposes", "Online subscription"));
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        var payment = await cloud.WithControlDbAsync(db => db.Payments.AsNoTracking().SingleAsync());
        Assert.Equal("Online subscription + POS licence + Set-up / installation", payment.Purpose);
        Assert.Equal(650, payment.Amount);

        // Nothing ticked is refused, not filed under a default.
        var none = await HtmlForms.PostAsync(c, page, page + "?handler=Payment",
            ("Pay.Amount", "10"), ("Pay.Date", "2026-11-03"), ("Pay.Purpose", ""));
        Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        Assert.Contains("Tick what the payment was for", await none.Content.ReadAsStringAsync());
        Assert.Equal(1, await cloud.WithControlDbAsync(db => db.Payments.CountAsync()));

        // The combined payment is one line in the Finance breakdown, and the admin API lists its purposes.
        Assert.Contains("Online subscription + POS licence + Set-up / installation", WebUtility.HtmlDecode(await c.GetStringAsync("/admin/Finance?m=2026-11")));
        var app = cloud.CreateClient();
        var login = await app.PostAsJsonAsync("/api/admin/v1/auth/login", new { username = CloudFactory.AdminUser, password = CloudFactory.AdminPassword });
        app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        var listed = (await app.GetFromJsonAsync<JsonElement>("/api/admin/v1/finance/payments?from=2026-11-01&to=2026-11-30"))[0];
        Assert.Equal(["Online subscription", "POS licence", "Set-up / installation"],
            listed.GetProperty("purposes").EnumerateArray().Select(x => x.GetString()).ToArray());

        // The API takes several too.
        var made = await app.PostAsJsonAsync("/api/admin/v1/finance/payments",
            new { clientId = seeded.ClientId, amount = 40, date = "2026-11-04", purposes = new[] { "Hardware", "Support" } });
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        Assert.Equal("Hardware + Support", (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("purpose").GetString());
    }

    [Fact]
    public async Task A_menu_only_client_pays_for_the_online_menu_and_its_renewal_shows_what_it_paid()
    {
        using var cloud = new CloudFactory();
        var c = await PortalAsync(cloud);
        var created = await HtmlForms.PostAsync(c, "/admin/Clients/Create", "/admin/Clients/Create",
            ("Name", "Menu Payer"), ("Type", "menu"), ("Months", "1"));
        var url = created.Headers.Location!.OriginalString;
        var id = int.Parse(url["/admin/Clients/Details/".Length..url.IndexOf('#')]);
        var page = $"/admin/Clients/Details/{id}";

        // Their page ticks "Online menu" to start with.
        var html = await c.GetStringAsync(page);
        Assert.Contains("value=\"Online menu\" checked=\"checked\"", html);
        Assert.DoesNotContain("value=\"Online subscription\" checked", html);

        await HtmlForms.PostAsync(c, page, page + "?handler=Payment",
            ("Pay.Amount", "90"), ("Pay.Date", "2026-11-05"), ("Pay.Purpose", ""), ("Pay.Purposes", "Online menu"));
        Assert.Equal("Online menu", await cloud.WithControlDbAsync(db => db.Payments.Select(p => p.Purpose).SingleAsync()));

        // The renewal is due within 30 days: Finance names it, with last time's 90 for the menu as the guide. A payment that also
        // covered something else would not be a guide, so the menu payment is matched on its own.
        var app = cloud.CreateClient();
        var login = await app.PostAsJsonAsync("/api/admin/v1/auth/login", new { username = CloudFactory.AdminUser, password = CloudFactory.AdminPassword });
        app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        var renewal = (await app.GetFromJsonAsync<JsonElement>("/api/admin/v1/finance/summary")).GetProperty("renewals").EnumerateArray()
            .Single(r => r.GetProperty("clientId").GetInt32() == id);
        Assert.Equal("Online menu ends", renewal.GetProperty("what").GetString());
        Assert.Equal(90, renewal.GetProperty("lastPaid").GetDecimal());
        Assert.Contains("Online menu ends", await c.GetStringAsync("/admin/Finance"));
    }
}
