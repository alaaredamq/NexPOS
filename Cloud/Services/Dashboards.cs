using System.Globalization;
using System.Text;
using Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Services;

/// <summary>One client as the overview lists it: the raw facts, plus the short phrases the portal shows beside them.</summary>
public sealed record ClientRow(
    int Id, string Name, bool IsActive, bool HasOnline, bool HasPos, string? Phone, string? Email, int Branches, string Sub,
    SubscriptionState State, DateTime? ExpiresAt, bool Suspended, string PlanNote,
    PulseData Pulse, DateTime? LastSeenAt, string Seen, string[] Tags, long? Products, long? Orders, bool EndingSoon);

/// <summary>The clients overview: every client with its plan and connection, the shops' contact over 24 hours, and what needs the provider.</summary>
public sealed record ClientOverview(
    IReadOnlyList<ClientRow> Rows, IReadOnlyList<AttentionItem> Attention, PulseData Overall,
    int Connected, int PlansActive, int PlansEnding, int PlansEnded, long ProductsTotal, long OrdersTotal, string Summary);

/// <summary>Builds the overview for the portal's Clients page and for the admin app (GET /api/admin/v1/overview and /clients).</summary>
public sealed class OverviewBuilder(ControlDbContext db, MirrorStore mirrors, PortalFormat fmt, LicenseBook licenses)
{
    private static readonly string[] CountTables = ["Products", "Orders"];

    public async Task<ClientOverview> BuildAsync()
    {
        var now = fmt.Now;
        var clients = await db.Clients.AsNoTracking()
            .Include(c => c.Subscription)
            .Include(c => c.Sites).ThenInclude(s => s.Tokens)
            .Include(c => c.Sites).ThenInclude(s => s.EnrollmentCodes)
            .OrderBy(c => c.Name).ToListAsync();

        var since = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-23);
        var beats = (await db.SiteHeartbeats.AsNoTracking().Where(h => h.Hour >= since).ToListAsync()).ToLookup(h => h.SiteId);
        var published = (await db.Menus.AsNoTracking().Where(m => m.IsPublished).Select(m => m.ClientId).ToListAsync()).ToHashSet();

        var attention = new List<AttentionItem>();
        var rows = clients.Select(c =>
        {
            var items = AttentionRules.For(c, now, fmt, published.Contains(c.Id));
            attention.AddRange(items);

            var states = c.Sites.Where(s => !s.IsDisabled).Select(s => SiteInsight.Connection(s, now)).ToList();
            var tags = new List<string>();
            if (!c.HasOnline) tags.Add("offline");
            if (c.HasOnline && !c.HasPos) tags.Add("menu");
            if (states.Contains(ConnectionState.Connected)) tags.Add("connected");
            if (c.HasOnline && c.HasPos && (states.Any(s => s is ConnectionState.WaitingForShop or ConnectionState.LinkExpired or ConnectionState.NotLinked) || c.Sites.Count == 0)) tags.Add("waiting");
            if (items.Count > 0) tags.Add("attention");

            // A client with several branches is one strip: an hour is lit when any of its branches was heard from.
            var pulse = PulseData.Build(c.Sites.SelectMany(s => beats[s.Id]), now);
            var seenAt = c.Sites.Where(s => s.LastSeenAt != null).Select(s => s.LastSeenAt).DefaultIfEmpty().Max();
            var state = SubscriptionRules.Evaluate(c.Subscription, now);

            // What the cloud holds for the client, summed over its branches. Null until some branch has uploaded.
            long? products = null, orders = null;
            foreach (var s in c.Sites.Where(s => mirrors.Exists(s.Id)))
            {
                var counts = mirrors.CountRows(s.Id, CountTables);
                products = (products ?? 0) + counts["Products"];
                orders = (orders ?? 0) + counts["Orders"];
            }

            var sub = !c.HasOnline && c.Sites.Count == 0 ? "POS only" : !c.HasPos && c.Sites.Count == 0 ? "Online menu only" : c.Sites.Count switch { 0 => "No branch yet", 1 => "1 branch", var n => $"{n} branches" };
            if (!string.IsNullOrWhiteSpace(c.Phone)) sub += " · " + c.Phone;

            return new ClientRow(c.Id, c.Name, c.IsActive, c.HasOnline, c.HasPos, c.Phone, c.Email, c.Sites.Count, sub, state,
                c.Subscription?.ExpiresAt, c.Subscription?.IsSuspended ?? false,
                c.Subscription is null ? "" : state is SubscriptionState.Active or SubscriptionState.Grace
                    ? $"ends {fmt.Until(c.Subscription.ExpiresAt)} · {fmt.Day(c.Subscription.ExpiresAt)}"
                    : $"ended {fmt.Day(c.Subscription.ExpiresAt)}",
                pulse, seenAt,
                seenAt is null ? (!c.HasOnline ? "Offline" : !c.HasPos ? "No POS" : c.Sites.Count == 0 ? "No branch yet" : "Not connected yet") : fmt.Ago(seenAt),
                tags.ToArray(), products, orders,
                state == SubscriptionState.Active && c.Subscription is not null && c.Subscription.ExpiresAt - now < TimeSpan.FromDays(14));
        }).ToList();

        // Licences ending or ended need the provider as much as an Online plan does.
        attention.AddRange(await licenses.AttentionAsync());
        var sorted = attention.OrderByDescending(a => a.Severity).ThenBy(a => a.Client).ToList();
        var connected = rows.Count(r => r.Tags.Contains("connected"));
        var ending = rows.Count(r => r.State == SubscriptionState.Active && r.EndingSoon);
        var active = rows.Count(r => r.State is SubscriptionState.Active or SubscriptionState.Grace) - ending;
        return new ClientOverview(rows, sorted,
            PulseData.Build(clients.SelectMany(c => c.Sites).SelectMany(x => beats[x.Id]), now),
            connected, active, ending, rows.Count - active - ending,
            rows.Sum(r => r.Products ?? 0), rows.Sum(r => r.Orders ?? 0),
            rows.Count == 0 ? "Nobody yet."
                : $"{rows.Count} client{(rows.Count == 1 ? "" : "s")} · {connected} connected"
                  + (sorted.Count > 0 ? $" · {sorted.Count} need{(sorted.Count == 1 ? "s" : "")} you" : ""));
    }
}

/// <summary>One payment (in) or expense (out) in the Finance list.</summary>
public sealed record FinanceLine(bool IsIncome, int Id, DateOnly Day, string Who, int? ClientId, string Details, string? Note, decimal Amount);

/// <summary>
/// Something ending soon that the client should pay to renew: their Online plan, a time-limited licence, or a Full licence's updates plan.
/// <see cref="LastPaid"/> is what they paid last time for that kind of thing, as a guide to what to expect.
/// </summary>
public sealed record Renewal(int ClientId, string Name, DateTime Ends, decimal? LastPaid, string What = "Online ends", int? LicenseId = null);

/// <summary>Everything the Finance dashboard shows for one period.</summary>
public sealed class FinanceView
{
    public required FinancePeriod Period { get; init; }
    public required FinanceReport Report { get; init; }
    /// <summary>The budget in force for the month shown; null for a year, or when none was ever set.</summary>
    public FinanceBudget? MonthBudget { get; init; }
    public required IReadOnlyList<FinanceLine> Lines { get; init; }
    public required IReadOnlyList<Renewal> Renewals { get; init; }
    public required IReadOnlyList<AttentionItem> Attention { get; init; }
    public required IReadOnlyList<Payment> Payments { get; init; }
    public required IReadOnlyList<Expense> Expenses { get; init; }
    public required IReadOnlyList<(int Id, string Name)> Clients { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
}

/// <summary>Builds the Finance dashboard for the portal's Finance page and the admin app (GET /api/admin/v1/finance/summary).</summary>
public sealed class FinanceDashboard(ControlDbContext db, PortalFormat fmt, LicenseBook licenses)
{
    public async Task<FinanceView> BuildAsync(FinancePeriod period)
    {
        var payments = await db.Payments.AsNoTracking().Include(x => x.Client).ToListAsync();
        var expenses = await db.Expenses.AsNoTracking().ToListAsync();
        var budgets = await db.FinanceBudgets.AsNoTracking().ToListAsync();
        var report = FinanceReport.Build(period, payments, expenses, budgets);

        var lines = payments.Where(x => period.Contains(x.ReceivedOn))
            .Select(x => new FinanceLine(true, x.Id, x.ReceivedOn, x.Client?.Name ?? x.Payer, x.ClientId,
                string.Join(" · ", new[] { x.Purpose, x.Method, x.Reference }.Where(s => !string.IsNullOrWhiteSpace(s))), x.Note, x.Amount))
            .Concat(expenses.Where(x => period.Contains(x.SpentOn))
                .Select(x => new FinanceLine(false, x.Id, x.SpentOn, x.Category, null, x.Payee ?? "", x.Note, x.Amount)))
            .OrderByDescending(x => x.Day).ThenByDescending(x => x.Id).ToList();

        var clients = await db.Clients.AsNoTracking().Include(c => c.Subscription).OrderBy(c => c.Name).ToListAsync();
        var now = fmt.Now;
        var renewals = clients
            .Where(c => c.IsActive && c.HasOnline && c.Subscription is { IsSuspended: false } s
                        && s.ExpiresAt >= now.AddDays(-SubscriptionRules.GraceDays) && s.ExpiresAt <= now.AddDays(30))
            .Select(c => new Renewal(c.Id, c.Name, c.Subscription!.ExpiresAt,
                payments.Where(x => x.ClientId == c.Id && x.Purpose == FinanceRules.Purposes[0]).MaxBy(x => x.ReceivedOn)?.Amount))
            .ToList();
        renewals = renewals.Concat(await licenses.RenewalsAsync()).OrderBy(x => x.Ends).ToList();

        var attention = new List<AttentionItem>();
        if (report.SpendingBudget is { Left: < 0 } over)
            attention.Add(new AttentionItem(AttentionSeverity.Bad, "Over the spending limit",
                $"Spent {fmt.Money(over.Actual)} in {period.Label}, {fmt.Money(-over.Left)} more than the {fmt.Money(over.Planned)} limit.", "", ""));

        // Clients whose Online plan is running but who have no payment on record for a year: either unpaid, or not written down here.
        var yearAgo = fmt.Today.AddYears(-1);
        var unpaid = clients.Where(c => c.IsActive && c.HasOnline && SubscriptionRules.Evaluate(c.Subscription, now) is SubscriptionState.Active or SubscriptionState.Grace
                                        && !payments.Any(x => x.ClientId == c.Id && x.ReceivedOn > yearAgo)).ToList();
        if (unpaid.Count > 0)
            attention.Add(new AttentionItem(AttentionSeverity.Warn,
                unpaid.Count == 1 ? $"{unpaid[0].Name} has no payment on record" : $"{unpaid.Count} clients with a running plan have no payment on record",
                (unpaid.Count == 1 ? "Their Online plan is running" : string.Join(", ", unpaid.Take(6).Select(c => c.Name)) + (unpaid.Count > 6 ? $" and {unpaid.Count - 6} more" : "") + ": their plans are running")
                + ", but nothing was recorded from them in the last 12 months. Record what they paid, or follow it up.",
                $"/admin/Clients/Details/{unpaid[0].Id}#payments", unpaid.Count == 1 ? "Open client" : "Open the first"));

        return new FinanceView
        {
            Period = period, Report = report,
            MonthBudget = period.IsYear ? null : FinanceRules.BudgetFor(budgets, period.From),
            Lines = lines, Renewals = renewals, Attention = attention, Payments = payments, Expenses = expenses,
            Clients = clients.Select(c => (c.Id, c.Name)).ToList(),
            Categories = FinanceRules.ExpenseCategories.Concat(expenses.Select(x => x.Category)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    /// <summary>The period's payments and expenses as a spreadsheet (UTF-8 with BOM, so Excel reads Arabic names): money in positive, out negative.</summary>
    public static byte[] Csv(FinanceView view)
    {
        var inv = CultureInfo.InvariantCulture;
        var csv = new StringBuilder("Date,Type,Amount,From / on,Method or paid to,Purpose,Reference,Note\r\n");
        foreach (var l in view.Lines.OrderBy(x => x.Day).ThenBy(x => x.Id))
        {
            var pay = l.IsIncome ? view.Payments.First(x => x.Id == l.Id) : null;
            var exp = l.IsIncome ? null : view.Expenses.First(x => x.Id == l.Id);
            csv.AppendJoin(',', new[]
            {
                l.Day.ToString("yyyy-MM-dd", inv), l.IsIncome ? "In" : "Out",
                (l.IsIncome ? l.Amount : -l.Amount).ToString("0.00", inv),
                Cell(l.Who), Cell(pay?.Method ?? exp?.Payee), Cell(pay?.Purpose), Cell(pay?.Reference), Cell(l.Note),
            }).Append("\r\n");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    public static string CsvName(FinancePeriod period) => period.IsYear ? $"finance-{period.From.Year}.csv" : $"finance-{period.From:yyyy-MM}.csv";

    /// <summary>Quoted when needed, and a leading = + - @ neutralised so a spreadsheet never runs a cell as a formula.</summary>
    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if ("=+-@\t\r".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
