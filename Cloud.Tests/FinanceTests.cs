using System.Net;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cloud.Tests;

/// <summary>The rules and sums behind the Finance dashboard, without a web server.</summary>
public class FinanceRulesTests
{
    [Theory]
    [InlineData("1250", 1250)]
    [InlineData("1,250.50", 1250.50)]
    [InlineData("$ 80", 80)]
    [InlineData(" 0.01 ", 0.01)]
    public void Amounts_are_read_as_typed(string text, decimal expected)
    {
        Assert.True(FinanceRules.TryParseAmount(text, out var amount));
        Assert.Equal(expected, amount);
    }

    // "Whish" (the old name) and any spelling of a usual method are saved one way, so totals by method do not split.
    [Theory]
    [InlineData("Whish", "Whish Money")]
    [InlineData(" whish money ", "Whish Money")]
    [InlineData("cash", "Cash")]
    [InlineData("Western Union", "Western Union")]
    [InlineData(null, "Other")]
    public void Paid_by_is_saved_with_one_spelling(string? typed, string saved) => Assert.Equal(saved, FinanceRules.Method(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("1.005")]
    [InlineData("10000001")]
    [InlineData("1.2.3")]
    [InlineData("-5")]
    [InlineData("12abc")]
    public void Nonsense_zero_and_absurd_amounts_are_refused(string? text)
        => Assert.False(FinanceRules.TryParseAmount(text, out _));

    [Fact]
    public void A_budget_carries_forward_until_a_later_one_replaces_it()
    {
        var budgets = new[]
        {
            new FinanceBudget { Month = new DateOnly(2026, 3, 1), IncomeTarget = 1000 },
            new FinanceBudget { Month = new DateOnly(2026, 8, 1), IncomeTarget = 2000, SpendingLimit = 500 },
        };
        Assert.Null(FinanceRules.BudgetFor(budgets, new DateOnly(2026, 2, 28)));
        Assert.Equal(1000, FinanceRules.BudgetFor(budgets, new DateOnly(2026, 7, 31))!.IncomeTarget);
        Assert.Equal(2000, FinanceRules.BudgetFor(budgets, new DateOnly(2026, 8, 15))!.IncomeTarget);
        Assert.Equal(2000, FinanceRules.BudgetFor(budgets, new DateOnly(2027, 5, 1))!.IncomeTarget);
    }

    [Theory]
    [InlineData("2026-11", null, "November 2026", false)]
    [InlineData(null, "2025", "2025", true)]
    [InlineData("garbage", null, "November 2026", false)]
    [InlineData("1999-01", "0", "November 2026", false)]
    public void The_period_comes_from_the_address_or_defaults_to_this_month(string? m, string? y, string label, bool year)
    {
        var p = FinancePeriod.Parse(m, y, new DateOnly(2026, 11, 5));
        Assert.Equal(label, p.Label);
        Assert.Equal(year, p.IsYear);
    }

    [Fact]
    public void The_report_adds_up_the_period_and_compares_it_with_the_one_before()
    {
        var acme = new Client { Id = 1, Name = "Acme" };
        var payments = new List<Payment>
        {
            new() { ClientId = 1, Client = acme, Payer = "Acme", Amount = 100, ReceivedOn = new(2026, 11, 2), Purpose = "Online subscription" },
            new() { ClientId = 1, Client = acme, Payer = "Old Acme name", Amount = 50, ReceivedOn = new(2026, 11, 20), Purpose = "Support" },
            new() { ClientId = null, Payer = "Capital", Amount = 1000, ReceivedOn = new(2026, 11, 1), Purpose = "Other" },
            new() { ClientId = 1, Client = acme, Payer = "Acme", Amount = 80, ReceivedOn = new(2026, 10, 3), Purpose = "Online subscription" },
            new() { ClientId = 1, Client = acme, Payer = "Acme", Amount = 999, ReceivedOn = new(2026, 12, 1), Purpose = "Online subscription" },
        };
        var expenses = new List<Expense>
        {
            new() { Amount = 30, SpentOn = new(2026, 11, 3), Category = "Hosting & servers" },
            new() { Amount = 20, SpentOn = new(2026, 11, 4), Category = "hosting & servers" },
            new() { Amount = 200, SpentOn = new(2026, 11, 9), Category = "Marketing" },
            new() { Amount = 40, SpentOn = new(2026, 10, 9), Category = "Marketing" },
        };
        var budgets = new List<FinanceBudget> { new() { Month = new(2026, 1, 1), IncomeTarget = 500, SpendingLimit = 200 } };

        var r = FinanceReport.Build(FinancePeriod.Month(new(2026, 11, 5)), payments, expenses, budgets);

        Assert.Equal(1150, r.Income);
        Assert.Equal(250, r.Expenses);
        Assert.Equal(900, r.Profit);
        Assert.Equal(78, r.Margin);
        Assert.Equal(80, r.PreviousIncome);
        Assert.Equal(40, r.PreviousExpenses);
        // Up to the end of November: everything but December's payment.
        Assert.Equal(1230 - 290, r.Balance);
        Assert.Equal(1, r.PayingClients);

        // One row per client, under its current name, even though a payment was recorded under an older one.
        Assert.Equal([("Capital", 1000m, 1), ("Acme", 150m, 2)], r.ByPayer.Select(x => (x.Name, x.Amount, x.Count)));
        // Categories group regardless of case.
        Assert.Equal([("Marketing", 200m), ("Hosting & servers", 50m)], r.ByCategory.Select(x => (x.Name, x.Amount)));

        Assert.Equal(500, r.IncomeBudget!.Planned);
        Assert.Equal(-50, r.SpendingBudget!.Left);
        Assert.Equal(100, r.SpendingBudget.Percent); // the meter is capped; the label carries the real figure
        Assert.Equal(125, r.SpendingBudget.PercentRounded);

        Assert.Equal(12, r.Trend.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), r.Trend[^1].Month);
        Assert.Equal(1150, r.Trend[^1].Income);
        Assert.Equal(80, r.Trend[^2].Income);
    }

    [Fact]
    public void A_year_adds_up_the_budget_in_force_each_month()
    {
        var budgets = new List<FinanceBudget>
        {
            new() { Month = new(2026, 1, 1), IncomeTarget = 100 },
            new() { Month = new(2026, 7, 1), IncomeTarget = 200, SpendingLimit = 50 },
        };
        var r = FinanceReport.Build(FinancePeriod.Year(2026), [], [], budgets);
        Assert.Equal(6 * 100 + 6 * 200, r.IncomeBudget!.Planned);
        Assert.Equal(6 * 50, r.SpendingBudget!.Planned);
    }

    [Theory]
    [InlineData(1250, "$1,250")]
    [InlineData(1250.5, "$1,250.50")]
    [InlineData(-300, "-$300")]
    public void Money_reads_plainly(decimal amount, string expected)
    {
        var fmt = new PortalFormat(Microsoft.Extensions.Options.Options.Create(new CloudOptions()), TimeProvider.System);
        Assert.Equal(expected, fmt.Money(amount));
    }
}

/// <summary>The Finance pages end to end: recording, budgets, the dashboard, the export, and what happens when a client goes.</summary>
public class FinancePortalTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public FinancePortalTests(CloudFactory cloud) => _cloud = cloud;

    private Task<HttpClient> SignedInAsync() => SignedInAsync(_cloud);

    private static async Task<HttpClient> SignedInAsync(CloudFactory cloud)
    {
        var c = cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var response = await HtmlForms.PostAsync(c, "/admin/Account/Login", "/admin/Account/Login",
            ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return c;
    }

    [Fact]
    public async Task Finance_needs_sign_in()
    {
        var anonymous = _cloud.CreateClient(new() { AllowAutoRedirect = false });
        var page = await anonymous.GetAsync("/admin/Finance");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/admin/Account/Login", page.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/admin/Finance?handler=Csv")).StatusCode);
    }

    [Fact]
    public async Task Payments_and_expenses_show_on_the_dashboard_and_in_the_export()
    {
        // Its own cloud: the totals below must not pick up other tests' payments.
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Finance Bakery");
        var c = await SignedInAsync(cloud);

        var paid = await HtmlForms.PostAsync(c, "/admin/Finance", "/admin/Finance?handler=Payment&m=2026-11",
            ("Pay.ClientId", seeded.ClientId.ToString()), ("Pay.Amount", "1,200"), ("Pay.Date", "2026-11-03"),
            ("Pay.Method", "Whish"), ("Pay.Purpose", "Online subscription"), ("Pay.Reference", "=HYPERLINK(\"x\")"));
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        Assert.Equal("/admin/Finance?m=2026-11", paid.Headers.Location!.OriginalString);

        var other = await HtmlForms.PostAsync(c, "/admin/Finance", "/admin/Finance?handler=Payment",
            ("Pay.ClientId", ""), ("Pay.Payer", "Owner capital"), ("Pay.Amount", "500"), ("Pay.Date", "2026-11-01"));
        Assert.Equal(HttpStatusCode.Redirect, other.StatusCode);

        var spent = await HtmlForms.PostAsync(c, "/admin/Finance", "/admin/Finance?handler=Expense",
            ("Exp.Amount", "300"), ("Exp.Date", "2026-11-04"), ("Exp.Category", "Hosting & servers"), ("Exp.Payee", "MonsterASP"));
        Assert.Equal(HttpStatusCode.Redirect, spent.StatusCode);

        var budget = await HtmlForms.PostAsync(c, "/admin/Finance", "/admin/Finance?handler=Budget",
            ("month", "2026-11"), ("target", "2000"), ("limit", "250"));
        Assert.Equal(HttpStatusCode.Redirect, budget.StatusCode);

        var page = await c.GetStringAsync("/admin/Finance?m=2026-11");
        Assert.Contains("Finance Bakery", page);
        Assert.Contains("Owner capital", page);
        Assert.Contains("MonsterASP", page);
        Assert.Contains("$1,700", page);   // collected
        Assert.Contains("$1,400", page);   // profit
        Assert.Contains("Over the spending limit", page);
        Assert.Contains("$50 over the limit", page);

        // The client's own history notes the payment, under the name it had.
        var stored = await cloud.WithControlDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.ClientId == seeded.ClientId));
        Assert.Equal("Finance Bakery", stored.Payer);
        Assert.Equal(CloudFactory.AdminUser, stored.CreatedBy);
        Assert.Contains("Payment recorded: $1,200", await c.GetStringAsync($"/admin/Clients/Details/{seeded.ClientId}"));

        var csv = await c.GetStringAsync("/admin/Finance?handler=Csv&m=2026-11");
        Assert.Contains("2026-11-03,In,1200.00,Finance Bakery,Whish Money,Online subscription,\"'=HYPERLINK(\"\"x\"\")\"", csv);
        Assert.Contains("2026-11-04,Out,-300.00,Hosting & servers,MonsterASP", csv);

        // Another month shows none of it, but the balance carries.
        var december = await c.GetStringAsync("/admin/Finance?m=2026-12");
        Assert.Contains("Nothing recorded in December 2026 yet.", december);
        Assert.Contains("$1,400", december);
    }

    [Fact]
    public async Task A_bad_amount_keeps_the_form_and_says_why()
    {
        var c = await SignedInAsync();
        var response = await HtmlForms.PostAsync(c, "/admin/Finance", "/admin/Finance?handler=Expense",
            ("Exp.Amount", "-5"), ("Exp.Date", "2026-11-04"), ("Exp.Category", "Marketing"), ("Exp.Payee", "Kept payee"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Enter the amount spent", html);
        Assert.Contains("value=\"Kept payee\"", html);

        var future = await HtmlForms.PostAsync(c, "/admin/Finance", "/admin/Finance?handler=Payment",
            ("Pay.Payer", "Someone"), ("Pay.Amount", "10"), ("Pay.Date", "2027-01-01"));
        Assert.Contains("cannot be in the future", await future.Content.ReadAsStringAsync());

        var nobody = await HtmlForms.PostAsync(c, "/admin/Finance", "/admin/Finance?handler=Payment",
            ("Pay.Amount", "10"), ("Pay.Date", "2026-11-01"));
        Assert.Contains("Pick the client who paid", await nobody.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_payment_recorded_on_the_client_page_is_theirs_and_only_their_page_deletes_it()
    {
        var mine = await _cloud.SeedSiteAsync(clientName: "Payer One");
        var theirs = await _cloud.SeedSiteAsync(clientName: "Payer Two");
        var c = await SignedInAsync();
        var url = $"/admin/Clients/Details/{mine.ClientId}";

        var paid = await HtmlForms.PostAsync(c, url, $"{url}?handler=Payment",
            ("Pay.ClientId", theirs.ClientId.ToString()), // the page's own client wins over a tampered field
            ("Pay.Amount", "75"), ("Pay.Date", "2026-11-02"), ("Pay.Purpose", "Online subscription"), ("Pay.Method", "Cash"));
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        var payment = await _cloud.WithControlDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.Payer == "Payer One"));
        Assert.Equal(mine.ClientId, payment.ClientId);

        var page = await c.GetStringAsync(url);
        Assert.Contains("collected in all", page);
        Assert.Contains("$75", page);

        // Deleting through the other client's page does nothing.
        var otherUrl = $"/admin/Clients/Details/{theirs.ClientId}";
        await HtmlForms.PostAsync(c, otherUrl, $"{otherUrl}?handler=DeletePayment&paymentId={payment.Id}");
        Assert.True(await _cloud.WithControlDbAsync(db => db.Payments.AnyAsync(p => p.Id == payment.Id)));

        await HtmlForms.PostAsync(c, url, $"{url}?handler=DeletePayment&paymentId={payment.Id}");
        Assert.False(await _cloud.WithControlDbAsync(db => db.Payments.AnyAsync(p => p.Id == payment.Id)));
    }

    [Fact]
    public async Task Removing_a_client_keeps_its_payments_in_the_books()
    {
        var seeded = await _cloud.SeedSiteAsync(clientName: "Leaving Shop");
        var c = await SignedInAsync();
        var url = $"/admin/Clients/Details/{seeded.ClientId}";
        await HtmlForms.PostAsync(c, url, $"{url}?handler=Payment", ("Pay.Amount", "40"), ("Pay.Date", "2026-11-02"));

        var removed = await HtmlForms.PostAsync(c, url, $"{url}?handler=Remove");
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);

        var kept = await _cloud.WithControlDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.Payer == "Leaving Shop"));
        Assert.Null(kept.ClientId);
        Assert.Equal(40, kept.Amount);
        Assert.Contains("Leaving Shop", await c.GetStringAsync("/admin/Finance?m=2026-11"));
    }

    [Fact]
    public async Task A_running_plan_with_nothing_on_record_is_flagged_and_renewals_due_are_listed()
    {
        var seeded = await _cloud.SeedSiteAsync(subscriptionMonths: 1, clientName: "Never Paid Market");
        var c = await SignedInAsync();
        var page = await c.GetStringAsync("/admin/Finance");
        Assert.Contains("Never Paid Market", page);
        Assert.Contains("no payment on record", page);
        Assert.Contains("Renewals due in the next 30 days", page);
        Assert.Contains($"/admin/Clients/Details/{seeded.ClientId}#payments", page);
    }
}
