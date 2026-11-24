using System.Globalization;
using Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Services;

/// <summary>The choices the Finance forms offer, and the small rules every Finance screen shares.</summary>
public static class FinanceRules
{
    public static readonly string[] Methods = ["Cash", "Bank transfer", "Whish", "OMT", "Card", "Cheque", "Other"];
    public static readonly string[] Purposes = ["Online subscription", "POS licence", "Set-up / installation", "Hardware", "Support", "Other"];
    public static readonly string[] ExpenseCategories =
        ["Hosting & servers", "Software & tools", "Marketing", "Salaries", "Hardware", "Transport", "Rent & utilities", "Fees & taxes", "Other"];

    /// <summary>The largest single amount a form accepts: catches a slipped finger ("50000000") before it skews every total.</summary>
    public const decimal MaxAmount = 10_000_000m;

    /// <summary>Reads "1,250", "1250.5", "$ 80" as typed. Refuses zero, negatives, more than two decimals and anything over <see cref="MaxAmount"/>.</summary>
    public static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        // Digits, a decimal point, thousands commas, spaces and a currency sign; anything else (a minus, letters) is refused, never dropped.
        if (text.Any(c => !(char.IsAsciiDigit(c) || c is '.' or ',' or ' ' or '$' or '€' or '£'))) return false;
        var cleaned = new string(text.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return false;
        if (value <= 0 || value > MaxAmount || value != Math.Round(value, 2)) return false;
        amount = value;
        return true;
    }

    /// <summary>The budget in force for <paramref name="month"/>: the latest one set for that month or before it.</summary>
    public static FinanceBudget? BudgetFor(IEnumerable<FinanceBudget> budgets, DateOnly month)
    {
        var first = FirstOfMonth(month);
        return budgets.Where(b => b.Month <= first).MaxBy(b => b.Month);
    }

    public static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>Trimmed, or null when blank, and cut to the column's length.</summary>
    public static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        return t.Length <= max ? t : t[..max];
    }
}

/// <summary>A month or a calendar year: what the Finance dashboard is looking at.</summary>
public sealed record FinancePeriod(DateOnly From, DateOnly To, bool IsYear)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static FinancePeriod Month(DateOnly anyDay)
    {
        var from = FinanceRules.FirstOfMonth(anyDay);
        return new(from, from.AddMonths(1).AddDays(-1), false);
    }

    public static FinancePeriod Year(int year) => new(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), true);

    /// <summary>From the query string: <c>y=2026</c> is a year, <c>m=2026-11</c> a month; anything else is the current month.</summary>
    public static FinancePeriod Parse(string? month, string? year, DateOnly today)
    {
        if (int.TryParse(year, NumberStyles.None, Inv, out var y) && y is >= 2000 and <= 2100) return Year(y);
        if (DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", Inv, DateTimeStyles.None, out var m) && m.Year is >= 2000 and <= 2100)
            return Month(m);
        return Month(today);
    }

    public string Label => IsYear ? From.Year.ToString(Inv) : From.ToString("MMMM yyyy", Inv);
    public string Query => IsYear ? $"y={From.Year}" : $"m={From:yyyy-MM}";
    public FinancePeriod Previous => IsYear ? Year(From.Year - 1) : Month(From.AddMonths(-1));
    public FinancePeriod Next => IsYear ? Year(From.Year + 1) : Month(From.AddMonths(1));
    public bool Contains(DateOnly day) => day >= From && day <= To;

    /// <summary>The first day of each month in the period (one for a month, twelve for a year).</summary>
    public IEnumerable<DateOnly> Months()
    {
        for (var m = From; m <= To; m = m.AddMonths(1)) yield return m;
    }
}

public sealed record MonthFigures(DateOnly Month, decimal Income, decimal Expenses)
{
    public decimal Profit => Income - Expenses;
}

public sealed record ShareRow(string Name, decimal Amount, int Count, int? ClientId = null);

/// <summary>A budget compared with what happened: how far along, and whether it was crossed.</summary>
public sealed record BudgetUse(decimal Planned, decimal Actual)
{
    /// <summary>0 to 100 for the meter (capped; the label carries the real figure).</summary>
    public double Percent => Planned <= 0 ? 0 : (double)Math.Min(100m, Actual * 100m / Planned);
    public int PercentRounded => Planned <= 0 ? 0 : (int)Math.Round(Actual * 100m / Planned, MidpointRounding.AwayFromZero);
    public decimal Left => Planned - Actual;
}

/// <summary>
/// Everything the Finance dashboard shows, computed in memory from the payments, expenses and budgets. A provider's books are
/// small (a few rows a week), and decimal sums cannot be pushed down to SQLite, so the rows are loaded and summed here.
/// </summary>
public sealed class FinanceReport
{
    public required FinancePeriod Period { get; init; }
    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
    public decimal Profit => Income - Expenses;
    /// <summary>Profit as a share of income, or null with no income.</summary>
    public int? Margin => Income <= 0 ? null : (int)Math.Round(Profit * 100m / Income, MidpointRounding.AwayFromZero);
    public decimal PreviousIncome { get; init; }
    public decimal PreviousExpenses { get; init; }
    public decimal PreviousProfit => PreviousIncome - PreviousExpenses;
    /// <summary>All money in minus all money out up to the end of the period: what the business should be holding.</summary>
    public decimal Balance { get; init; }
    public int PayingClients { get; init; }
    public BudgetUse? IncomeBudget { get; init; }
    public BudgetUse? SpendingBudget { get; init; }
    /// <summary>The twelve months ending with the period's last month.</summary>
    public required IReadOnlyList<MonthFigures> Trend { get; init; }
    public required IReadOnlyList<ShareRow> ByPayer { get; init; }
    public required IReadOnlyList<ShareRow> ByPurpose { get; init; }
    public required IReadOnlyList<ShareRow> ByCategory { get; init; }

    public decimal TrendAverageIncome => Trend.Count == 0 ? 0 : Math.Round(Trend.Average(t => t.Income), 2);
    public decimal TrendAverageExpenses => Trend.Count == 0 ? 0 : Math.Round(Trend.Average(t => t.Expenses), 2);

    public static FinanceReport Build(FinancePeriod period, IReadOnlyCollection<Payment> payments, IReadOnlyCollection<Expense> expenses,
        IReadOnlyCollection<FinanceBudget> budgets)
    {
        var prev = period.Previous;
        var inPeriod = payments.Where(p => period.Contains(p.ReceivedOn)).ToList();
        var spent = expenses.Where(e => period.Contains(e.SpentOn)).ToList();

        // Budgets are monthly; a year adds up the budget in force in each of its months.
        decimal? target = null, limit = null;
        foreach (var month in period.Months())
        {
            var b = FinanceRules.BudgetFor(budgets, month);
            if (b?.IncomeTarget is { } t) target = (target ?? 0) + t;
            if (b?.SpendingLimit is { } l) limit = (limit ?? 0) + l;
        }

        var income = inPeriod.Sum(p => p.Amount);
        var outgo = spent.Sum(e => e.Amount);

        var lastMonth = FinanceRules.FirstOfMonth(period.To);
        var trend = Enumerable.Range(0, 12).Select(i => lastMonth.AddMonths(i - 11)).Select(m =>
        {
            var range = FinancePeriod.Month(m);
            return new MonthFigures(m,
                payments.Where(p => range.Contains(p.ReceivedOn)).Sum(p => p.Amount),
                expenses.Where(e => range.Contains(e.SpentOn)).Sum(e => e.Amount));
        }).ToList();

        return new FinanceReport
        {
            Period = period,
            Income = income,
            Expenses = outgo,
            PreviousIncome = payments.Where(p => prev.Contains(p.ReceivedOn)).Sum(p => p.Amount),
            PreviousExpenses = expenses.Where(e => prev.Contains(e.SpentOn)).Sum(e => e.Amount),
            Balance = payments.Where(p => p.ReceivedOn <= period.To).Sum(p => p.Amount)
                      - expenses.Where(e => e.SpentOn <= period.To).Sum(e => e.Amount),
            PayingClients = inPeriod.Where(p => p.ClientId != null).Select(p => p.ClientId).Distinct().Count(),
            IncomeBudget = target is null ? null : new BudgetUse(target.Value, income),
            SpendingBudget = limit is null ? null : new BudgetUse(limit.Value, outgo),
            Trend = trend,
            // A client is one row even if it was renamed between payments; other income groups by the name typed.
            ByPayer = inPeriod.GroupBy(p => p.ClientId is { } id ? "c" + id : "n" + p.Payer.ToLowerInvariant())
                .Select(g =>
                {
                    var latest = g.MaxBy(p => p.ReceivedOn)!;
                    return new ShareRow(latest.Client?.Name ?? latest.Payer, g.Sum(p => p.Amount), g.Count(), latest.ClientId);
                })
                .OrderByDescending(r => r.Amount).ThenBy(r => r.Name).ToList(),
            ByPurpose = Group(inPeriod, p => p.Purpose, p => p.Amount),
            ByCategory = Group(spent, e => e.Category, e => e.Amount),
        };
    }

    private static List<ShareRow> Group<T>(IEnumerable<T> rows, Func<T, string> key, Func<T, decimal> amount)
        => rows.GroupBy(r => string.IsNullOrWhiteSpace(key(r)) ? "Other" : key(r).Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ShareRow(g.Key, g.Sum(amount), g.Count()))
            .OrderByDescending(r => r.Amount).ThenBy(r => r.Name).ToList();

    /// <summary>"+12%" / "-8%" against the previous period, or null when there is nothing to compare with.</summary>
    public static string? Change(decimal now, decimal before)
    {
        if (before == 0) return null;
        var pct = (int)Math.Round((now - before) * 100m / Math.Abs(before), MidpointRounding.AwayFromZero);
        return (pct > 0 ? "+" : "") + pct.ToString(CultureInfo.InvariantCulture) + "%";
    }
}

/// <summary>What the "Record payment" form sends (the Finance page and a client's page share it).</summary>
public sealed class PaymentInput
{
    /// <summary>Empty for other income.</summary>
    public int? ClientId { get; set; }
    /// <summary>Who paid, for income that is not from a client.</summary>
    public string? Payer { get; set; }
    public string? Amount { get; set; }
    public DateOnly? Date { get; set; }
    public string? Method { get; set; } = FinanceRules.Methods[0];
    public string? Purpose { get; set; } = FinanceRules.Purposes[0];
    public string? Reference { get; set; }
    public string? Note { get; set; }
}

/// <summary>What the "Record expense" form sends.</summary>
public sealed class ExpenseInput
{
    public string? Amount { get; set; }
    public DateOnly? Date { get; set; }
    public string? Category { get; set; }
    public string? Payee { get; set; }
    public string? Note { get; set; }
}

/// <summary>Writes to the books: checks a form, saves the row, and notes a client's payments in that client's history.</summary>
public sealed class FinanceBook(ControlDbContext db, PortalFormat fmt)
{
    /// <summary>Saves the payment, or returns why it cannot (in words for the form).</summary>
    public async Task<(Payment? Payment, string? Error)> RecordPaymentAsync(PaymentInput input, string by)
    {
        if (!FinanceRules.TryParseAmount(input.Amount, out var amount))
            return (null, $"Enter the amount received, more than 0 and at most {fmt.Money(FinanceRules.MaxAmount)}, with at most 2 decimals.");
        if (input.Date is not { } date) return (null, "Enter the day the money came in.");
        if (date > fmt.Today.AddDays(1)) return (null, "The day the money came in cannot be in the future.");

        Client? client = null;
        if (input.ClientId is { } clientId)
        {
            client = await db.Clients.FindAsync(clientId);
            if (client is null) return (null, "That client no longer exists.");
        }
        var payer = client?.Name ?? FinanceRules.Clean(input.Payer, 200);
        if (payer is null) return (null, "Pick the client who paid, or say where the money came from.");

        var payment = new Payment
        {
            ClientId = client?.Id, Payer = payer, Amount = amount, ReceivedOn = date,
            Method = FinanceRules.Clean(input.Method, 40) ?? "Other",
            Purpose = FinanceRules.Clean(input.Purpose, 60) ?? "Other",
            Reference = FinanceRules.Clean(input.Reference, 100), Note = FinanceRules.Clean(input.Note, 500),
            CreatedAt = fmt.Now, CreatedBy = by,
        };
        db.Payments.Add(payment);
        if (client is not null)
            db.ClientEvents.Add(new ClientEvent { ClientId = client.Id, At = fmt.Now,
                Text = $"Payment recorded: {fmt.Money(amount)} on {date:yyyy-MM-dd} · {payment.Method} · {payment.Purpose}." });
        await db.SaveChangesAsync();
        return (payment, null);
    }

    public async Task<(Expense? Expense, string? Error)> RecordExpenseAsync(ExpenseInput input, string by)
    {
        if (!FinanceRules.TryParseAmount(input.Amount, out var amount))
            return (null, $"Enter the amount spent, more than 0 and at most {fmt.Money(FinanceRules.MaxAmount)}, with at most 2 decimals.");
        if (input.Date is not { } date) return (null, "Enter the day it was paid.");
        if (date > fmt.Today.AddDays(1)) return (null, "The day it was paid cannot be in the future.");
        var category = FinanceRules.Clean(input.Category, 60);
        if (category is null) return (null, "Pick what the money was spent on.");

        var expense = new Expense
        {
            Amount = amount, SpentOn = date, Category = category,
            Payee = FinanceRules.Clean(input.Payee, 120), Note = FinanceRules.Clean(input.Note, 500),
            CreatedAt = fmt.Now, CreatedBy = by,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return (expense, null);
    }

    /// <summary>Removes a payment recorded by mistake. Returns it (for the redirect), or null when it was already gone.</summary>
    public async Task<Payment?> DeletePaymentAsync(int id)
    {
        var payment = await db.Payments.FindAsync(id);
        if (payment is null) return null;
        db.Payments.Remove(payment);
        if (payment.ClientId is { } clientId)
            db.ClientEvents.Add(new ClientEvent { ClientId = clientId, At = fmt.Now,
                Text = $"Payment of {fmt.Money(payment.Amount)} from {payment.ReceivedOn:yyyy-MM-dd} deleted." });
        await db.SaveChangesAsync();
        return payment;
    }

    public async Task<Expense?> DeleteExpenseAsync(int id)
    {
        var expense = await db.Expenses.FindAsync(id);
        if (expense is null) return null;
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync();
        return expense;
    }

    /// <summary>
    /// Sets the budget from <paramref name="month"/> on. Blank means "none": an empty target stops the earlier one carrying into
    /// this month, which is how a budget is switched off.
    /// </summary>
    public async Task<string?> SetBudgetAsync(DateOnly month, string? target, string? limit)
    {
        decimal? t = null, l = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            if (!FinanceRules.TryParseAmount(target, out var v)) return "The income target must be a positive amount.";
            t = v;
        }
        if (!string.IsNullOrWhiteSpace(limit))
        {
            if (!FinanceRules.TryParseAmount(limit, out var v)) return "The spending limit must be a positive amount.";
            l = v;
        }

        var first = FinanceRules.FirstOfMonth(month);
        var row = await db.FinanceBudgets.SingleOrDefaultAsync(b => b.Month == first);
        if (row is null) db.FinanceBudgets.Add(row = new FinanceBudget { Month = first });
        row.IncomeTarget = t;
        row.SpendingLimit = l;
        row.UpdatedAt = fmt.Now;
        await db.SaveChangesAsync();
        return null;
    }
}

/// <summary>
/// The fields of the "Record payment" form. On the Finance page the client is picked from <see cref="Clients"/> (or none, for other
/// income); on a client's own page <see cref="FixedClientId"/> is set and the picker is left out.
/// </summary>
public sealed record PaymentFieldsView(PaymentInput Input, IReadOnlyList<(int Id, string Name)> Clients, DateOnly Today, int? FixedClientId = null);
