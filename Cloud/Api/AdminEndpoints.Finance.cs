using System.Globalization;
using Cloud.Data;
using Cloud.Services;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Api;

/// <summary>The books: payments in, expenses out, monthly budgets, and the Finance dashboard.</summary>
public static partial class AdminEndpoints
{
    private static void MapFinance(RouteGroupBuilder g)
    {
        var f = g.MapGroup("/finance");
        f.MapGet("/options", Options);
        f.MapGet("/summary", Summary);
        f.MapGet("/export", Export);

        f.MapGet("/payments", ListPayments);
        f.MapGet("/payments/{id:int}", GetPayment);
        f.MapPost("/payments", AddPayment);
        f.MapPut("/payments/{id:int}", EditPayment);
        f.MapDelete("/payments/{id:int}", DeletePayment);
        g.MapGet("/clients/{id:int}/payments", ClientPayments);

        f.MapGet("/expenses", ListExpenses);
        f.MapGet("/expenses/{id:int}", GetExpense);
        f.MapPost("/expenses", AddExpense);
        f.MapPut("/expenses/{id:int}", EditExpense);
        f.MapDelete("/expenses/{id:int}", DeleteExpense);

        f.MapGet("/budgets", ListBudgets);
        f.MapGet("/budgets/{month}", GetBudget);
        f.MapPut("/budgets/{month}", SetBudget);
        f.MapDelete("/budgets/{month}", DeleteBudget);
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static object Payment(Payment p) => new
    {
        id = p.Id, clientId = p.ClientId, payer = p.Payer, amount = p.Amount, date = Iso(p.ReceivedOn),
        method = p.Method, purpose = p.Purpose, reference = p.Reference, note = p.Note,
        createdAtUtc = p.CreatedAt, createdBy = p.CreatedBy,
    };

    private static object Expense(Expense e) => new
    {
        id = e.Id, amount = e.Amount, date = Iso(e.SpentOn), category = e.Category, payee = e.Payee, note = e.Note,
        createdAtUtc = e.CreatedAt, createdBy = e.CreatedBy,
    };

    private static object Budget(FinanceBudget b) => new
    {
        month = b.Month.ToString("yyyy-MM", Inv), incomeTarget = b.IncomeTarget, spendingLimit = b.SpendingLimit, updatedAtUtc = b.UpdatedAt,
    };

    private static bool TryMonth(string? text, out DateOnly month)
        => DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", Inv, DateTimeStyles.None, out month) && month.Year is >= 2000 and <= 2100;

    /// <summary>What the forms offer, and the currency amounts are in.</summary>
    private static async Task<IResult> Options(ControlDbContext db, PortalFormat fmt)
    {
        var used = await db.Expenses.AsNoTracking().Select(e => e.Category).Distinct().ToListAsync();
        return Json(new
        {
            currency = fmt.Currency,
            methods = FinanceRules.Methods,
            purposes = FinanceRules.Purposes,
            expenseCategories = FinanceRules.ExpenseCategories.Concat(used).Distinct(StringComparer.OrdinalIgnoreCase),
            maxAmount = FinanceRules.MaxAmount,
            today = Iso(fmt.Today),
        });
    }

    private static object Share(ShareRow r) => new { name = r.Name, amount = r.Amount, count = r.Count, clientId = r.ClientId };

    private static object? Use(BudgetUse? u) => u is null ? null : new
    {
        planned = u.Planned, actual = u.Actual, left = u.Left, percent = u.PercentRounded, over = u.Left < 0,
    };

    /// <summary>The Finance dashboard for <c>m=yyyy-MM</c> or <c>y=yyyy</c> (default: this month), with its list of payments and expenses.</summary>
    private static async Task<IResult> Summary(FinanceDashboard dashboard, PortalFormat fmt, string? m, string? y)
    {
        var view = await dashboard.BuildAsync(FinancePeriod.Parse(m, y, fmt.Today));
        var r = view.Report;
        var p = view.Period;
        return Json(new
        {
            period = new
            {
                kind = p.IsYear ? "year" : "month", label = p.Label, from = Iso(p.From), to = Iso(p.To), query = p.Query,
                previous = p.Previous.Query, next = p.Next.Query,
            },
            currency = fmt.Currency,
            collected = r.Income, spent = r.Expenses, profit = r.Profit, marginPercent = r.Margin, balance = r.Balance,
            previous = new { collected = r.PreviousIncome, spent = r.PreviousExpenses, profit = r.PreviousProfit },
            change = new
            {
                collected = FinanceReport.Change(r.Income, r.PreviousIncome),
                spent = FinanceReport.Change(r.Expenses, r.PreviousExpenses),
            },
            payingClients = r.PayingClients,
            budget = new
            {
                monthBudget = view.MonthBudget is null ? null : Budget(view.MonthBudget),
                income = Use(r.IncomeBudget),
                spending = Use(r.SpendingBudget),
            },
            trend = r.Trend.Select(t => new { month = t.Month.ToString("yyyy-MM", Inv), collected = t.Income, spent = t.Expenses, profit = t.Profit }),
            averages = new { collected = r.TrendAverageIncome, spent = r.TrendAverageExpenses, profit = r.TrendAverageIncome - r.TrendAverageExpenses },
            byPayer = r.ByPayer.Select(Share),
            byPurpose = r.ByPurpose.Select(Share),
            byCategory = r.ByCategory.Select(Share),
            renewals = view.Renewals.Select(x => new { clientId = x.ClientId, name = x.Name, what = x.What, endsAtUtc = x.Ends, lastPaid = x.LastPaid, licenseId = x.LicenseId }),
            renewalsExpected = view.Renewals.Sum(x => x.LastPaid ?? 0),
            attention = view.Attention.Select(Attention),
            lines = view.Lines.Select(l => new
            {
                type = l.IsIncome ? "payment" : "expense", id = l.Id, date = Iso(l.Day), who = l.Who, clientId = l.ClientId,
                details = l.Details, note = l.Note, amount = l.Amount,
            }),
        });
    }

    private static async Task<IResult> Export(FinanceDashboard dashboard, PortalFormat fmt, string? m, string? y)
    {
        var view = await dashboard.BuildAsync(FinancePeriod.Parse(m, y, fmt.Today));
        return Results.File(FinanceDashboard.Csv(view), "text/csv", FinanceDashboard.CsvName(view.Period));
    }

    // ---- payments ----

    /// <summary>A payment as JSON: <c>amount</c> is a number; <c>date</c> is yyyy-MM-dd; no <c>clientId</c> means other income (then <c>payer</c> says from whom).</summary>
    public sealed record PaymentRequest(int? ClientId, string? Payer, decimal? Amount, DateOnly? Date, string? Method, string? Purpose, string? Reference, string? Note)
    {
        public PaymentInput ToInput(PortalFormat fmt) => new()
        {
            ClientId = ClientId, Payer = Payer, Amount = Amount?.ToString(Inv), Date = Date ?? fmt.Today,
            Method = Method ?? FinanceRules.Methods[0], Purpose = Purpose ?? FinanceRules.Purposes[0], Reference = Reference, Note = Note,
        };
    }

    /// <summary>Payments between <c>from</c> and <c>to</c> (inclusive, yyyy-MM-dd), optionally for one client; newest first.</summary>
    private static async Task<IResult> ListPayments(ControlDbContext db, DateOnly? from, DateOnly? to, int? clientId)
    {
        var q = db.Payments.AsNoTracking();
        if (from is { } f) q = q.Where(p => p.ReceivedOn >= f);
        if (to is { } t) q = q.Where(p => p.ReceivedOn <= t);
        if (clientId is { } c) q = q.Where(p => p.ClientId == c);
        var rows = await q.ToListAsync();
        return Json(rows.OrderByDescending(p => p.ReceivedOn).ThenByDescending(p => p.Id).Select(Payment));
    }

    private static async Task<IResult> ClientPayments(int id, ControlDbContext db)
        => await db.Clients.AnyAsync(c => c.Id == id) ? await ListPayments(db, null, null, id) : NotFound("That client");

    private static async Task<IResult> GetPayment(int id, ControlDbContext db)
        => await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id) is { } p ? Json(Payment(p)) : NotFound("That payment");

    private static async Task<IResult> AddPayment(PaymentRequest body, HttpContext http, FinanceBook book, PortalFormat fmt)
    {
        var (payment, error) = await book.RecordPaymentAsync(body.ToInput(fmt), Who(http));
        return payment is null ? Refused("invalid_payment", error!) : Json(Payment(payment), StatusCodes.Status201Created);
    }

    private static async Task<IResult> EditPayment(int id, PaymentRequest body, FinanceBook book, PortalFormat fmt)
    {
        var (payment, error) = await book.UpdatePaymentAsync(id, body.ToInput(fmt));
        return payment is not null ? Json(Payment(payment)) : error is null ? NotFound("That payment") : Refused("invalid_payment", error);
    }

    private static async Task<IResult> DeletePayment(int id, FinanceBook book)
        => await book.DeletePaymentAsync(id) is null ? NotFound("That payment") : Results.NoContent();

    // ---- expenses ----

    public sealed record ExpenseRequest(decimal? Amount, DateOnly? Date, string? Category, string? Payee, string? Note)
    {
        public ExpenseInput ToInput(PortalFormat fmt) => new()
        {
            Amount = Amount?.ToString(Inv), Date = Date ?? fmt.Today, Category = Category, Payee = Payee, Note = Note,
        };
    }

    private static async Task<IResult> ListExpenses(ControlDbContext db, DateOnly? from, DateOnly? to, string? category)
    {
        var q = db.Expenses.AsNoTracking();
        if (from is { } f) q = q.Where(e => e.SpentOn >= f);
        if (to is { } t) q = q.Where(e => e.SpentOn <= t);
        var rows = await q.ToListAsync();
        if (!string.IsNullOrWhiteSpace(category)) rows = rows.Where(e => string.Equals(e.Category, category.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return Json(rows.OrderByDescending(e => e.SpentOn).ThenByDescending(e => e.Id).Select(Expense));
    }

    private static async Task<IResult> GetExpense(int id, ControlDbContext db)
        => await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id) is { } e ? Json(Expense(e)) : NotFound("That expense");

    private static async Task<IResult> AddExpense(ExpenseRequest body, HttpContext http, FinanceBook book, PortalFormat fmt)
    {
        var (expense, error) = await book.RecordExpenseAsync(body.ToInput(fmt), Who(http));
        return expense is null ? Refused("invalid_expense", error!) : Json(Expense(expense), StatusCodes.Status201Created);
    }

    private static async Task<IResult> EditExpense(int id, ExpenseRequest body, FinanceBook book, PortalFormat fmt)
    {
        var (expense, error) = await book.UpdateExpenseAsync(id, body.ToInput(fmt));
        return expense is not null ? Json(Expense(expense)) : error is null ? NotFound("That expense") : Refused("invalid_expense", error);
    }

    private static async Task<IResult> DeleteExpense(int id, FinanceBook book)
        => await book.DeleteExpenseAsync(id) is null ? NotFound("That expense") : Results.NoContent();

    // ---- budgets ----

    public sealed record BudgetRequest(decimal? IncomeTarget, decimal? SpendingLimit);

    /// <summary>Every budget that was set, oldest first. Each applies from its month until the next one.</summary>
    private static async Task<IResult> ListBudgets(ControlDbContext db)
        => Json((await db.FinanceBudgets.AsNoTracking().ToListAsync()).OrderBy(b => b.Month).Select(Budget));

    /// <summary>The budget in force for a month (yyyy-MM), which may have been set in an earlier month. <c>setIn</c> says which.</summary>
    private static async Task<IResult> GetBudget(string month, ControlDbContext db)
    {
        if (!TryMonth(month, out var m)) return Refused("bad_month", "The month must look like 2026-11.");
        var b = FinanceRules.BudgetFor(await db.FinanceBudgets.AsNoTracking().ToListAsync(), m);
        return Json(new
        {
            month = m.ToString("yyyy-MM", Inv), incomeTarget = b?.IncomeTarget, spendingLimit = b?.SpendingLimit,
            setIn = b?.Month.ToString("yyyy-MM", Inv),
        });
    }

    /// <summary>Sets the budget from that month on. A null (or missing) amount means none, which stops an earlier one carrying into this month.</summary>
    private static async Task<IResult> SetBudget(string month, BudgetRequest body, FinanceBook book, ControlDbContext db)
    {
        if (!TryMonth(month, out var m)) return Refused("bad_month", "The month must look like 2026-11.");
        var error = await book.SetBudgetAsync(m, body.IncomeTarget?.ToString(Inv), body.SpendingLimit?.ToString(Inv));
        if (error is not null) return Refused("invalid_budget", error);
        return Json(Budget(await db.FinanceBudgets.AsNoTracking().FirstAsync(b => b.Month == m)));
    }

    /// <summary>Removes the budget set in that month, so the one before it applies again.</summary>
    private static async Task<IResult> DeleteBudget(string month, FinanceBook book)
    {
        if (!TryMonth(month, out var m)) return Refused("bad_month", "The month must look like 2026-11.");
        return await book.DeleteBudgetAsync(m) ? Results.NoContent() : NotFound("A budget set in that month");
    }
}
