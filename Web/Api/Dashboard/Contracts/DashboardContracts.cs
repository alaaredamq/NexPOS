namespace Web.Api.Dashboard.Contracts;

// Contract of track I1 (Dashboard). Mirrors the Razor DashboardController; see docs/react-migration/notes/I1.md for the differences.

/// <summary>The periods the sales chart can show.</summary>
public static class DashboardPeriod
{
    /// <summary>The last 7 days, one point per day.</summary>
    public const string Week = "week";

    /// <summary>From the first of the current month, one point per day. The default.</summary>
    public const string Month = "month";

    /// <summary>From the first of the current quarter, one point per month.</summary>
    public const string Quarter = "quarter";

    /// <summary>From 1 January, one point per month.</summary>
    public const string Year = "year";

    /// <summary>Everything since the first sale, one point per month.</summary>
    public const string AllTime = "all";

    public static readonly string[] All = { Week, Month, Quarter, Year, AllTime };
}

/// <summary>Everything the dashboard page shows except the chart: the tiles, the latest orders and the category split.</summary>
public sealed record DashboardDto
{
    /// <summary>Notation of the default currency (<c>$</c>); every amount on this payload is in that currency.</summary>
    public required string CurrencyNotation { get; init; }

    /// <summary>Sales from the first of this month until now (cancelled and returned orders excluded).</summary>
    public required decimal MonthSales { get; init; }

    /// <summary>Change of <see cref="MonthSales"/> against the same number of days of last month, in percent; 0 when last month had no sales.</summary>
    public required decimal MonthSalesChangePercent { get; init; }

    public required int MonthOrders { get; init; }

    /// <summary>Orders this month minus orders in the same days of last month (a count, not a percentage).</summary>
    public required int MonthOrdersChange { get; init; }

    public required decimal TodaySales { get; init; }

    public required int TodayOrders { get; init; }

    /// <summary>Gross profit this month: (price - cost price) x quantity over the lines whose product has a cost price.</summary>
    public required decimal MonthProfit { get; init; }

    /// <summary>Change of <see cref="MonthProfit"/> against last month, in percent; 0 when last month had no profit.</summary>
    public required decimal MonthProfitChangePercent { get; init; }

    /// <summary>Revenue of every completed order since the first sale (cancelled and returned orders excluded).</summary>
    public required decimal AllTimeSales { get; init; }

    /// <summary>Gross profit since the first sale, counted like <see cref="MonthProfit"/>.</summary>
    public required decimal AllTimeProfit { get; init; }

    public required int CustomerCount { get; init; }

    public required int ProductCount { get; init; }

    /// <summary>The 10 newest orders of the last 90 days, any status.</summary>
    public required IReadOnlyList<DashboardOrderDto> RecentOrders { get; init; }

    /// <summary>Sales per category this month, biggest first (at most 10). Empty when nothing was sold.</summary>
    public required IReadOnlyList<DashboardCategoryDto> Categories { get; init; }
}

/// <summary>One of the latest orders.</summary>
public sealed record DashboardOrderDto
{
    public required int Id { get; init; }

    /// <summary>The customer's name, or null for a walk-in sale (the UI shows its own "Walk-in" text).</summary>
    public string? CustomerName { get; init; }

    public required decimal Amount { get; init; }

    /// <summary>Notation of the currency of <see cref="Amount"/> (the default currency, as before).</summary>
    public required string CurrencyNotation { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime OrderDate { get; init; }

    /// <summary>The order status as the Orders API numbers it: 0 Pending, 1 Processing, 2 Shipped, 3 Delivered, 4 Cancelled, 5 Returned.</summary>
    public required int Status { get; init; }

    /// <summary>Payment method of the receipt, or null when the order has none.</summary>
    public string? PaymentMethod { get; init; }
}

/// <summary>One slice of this month's sales by category.</summary>
public sealed record DashboardCategoryDto
{
    public required string Name { get; init; }

    public required decimal Sales { get; init; }

    /// <summary>Share of this month's categorised sales, 0 to 100 (the 10 listed categories can add up to less than 100).</summary>
    public required decimal Percentage { get; init; }
}

/// <summary>The sales chart: revenue and gross profit per day (week, month) or per month (quarter, year).</summary>
public sealed record SalesTrendDto
{
    public required string Period { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>One entry per bucket that has sales, oldest first.</summary>
    public required IReadOnlyList<SalesTrendPointDto> Points { get; init; }
}

/// <summary>One bucket of the sales chart.</summary>
public sealed record SalesTrendPointDto
{
    /// <summary>First local day of the bucket (the day itself, or the 1st of the month), as <c>yyyy-MM-dd</c>. The UI formats it in the user's language.</summary>
    public required string Date { get; init; }

    public required decimal Sales { get; init; }

    /// <summary>Sales minus the cost price of the lines sold; lines whose product has no cost price count as 0 cost.</summary>
    public required decimal Profit { get; init; }
}
