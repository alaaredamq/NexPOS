namespace Web.Api.Reports.Contracts;

// Contract of track I1 (Reports). Mirrors the Razor ReportsController + SalesReportService; see docs/react-migration/notes/I1.md for the differences.

/// <summary>Sales so far today, this month and this year (local calendar periods) - the tiles at the top of the Reports page.</summary>
public sealed record ReportsSummaryDto
{
    /// <summary>Notation of the default currency; every amount on this payload is in that currency.</summary>
    public required string CurrencyNotation { get; init; }

    public required SalesPeriodDto Today { get; init; }

    public required SalesPeriodDto ThisMonth { get; init; }

    public required SalesPeriodDto ThisYear { get; init; }

    /// <summary>This month's sales divided by this month's orders; 0 without orders.</summary>
    public required decimal AverageOrderValue { get; init; }
}

/// <summary>Sales and order count of one period (cancelled and returned orders are not counted).</summary>
public sealed record SalesPeriodDto
{
    public required decimal Sales { get; init; }

    public required int Orders { get; init; }
}

/// <summary>The sales report of a range of local days.</summary>
public sealed record SalesReportDto
{
    /// <summary>First day of the range, <c>yyyy-MM-dd</c>.</summary>
    public required string From { get; init; }

    /// <summary>Last day of the range (inclusive), <c>yyyy-MM-dd</c>.</summary>
    public required string To { get; init; }

    public required string CurrencyNotation { get; init; }

    public required decimal TotalSales { get; init; }

    public required int TotalOrders { get; init; }

    public required decimal AverageOrderValue { get; init; }

    /// <summary>Sum of the line quantities (a weighed line counts its piece quantity, as before).</summary>
    public required decimal TotalItemsSold { get; init; }

    /// <summary>One entry per local day that has sales, oldest first.</summary>
    public required IReadOnlyList<SalesReportDayDto> Daily { get; init; }

    /// <summary>The 10 best sellers by quantity.</summary>
    public required IReadOnlyList<SalesReportProductDto> TopProducts { get; init; }

    /// <summary>Sales per category, biggest first.</summary>
    public required IReadOnlyList<SalesReportCategoryDto> Categories { get; init; }

    /// <summary>Sales per payment method of the receipts in the range (void receipts left out), biggest first.</summary>
    public required IReadOnlyList<SalesReportPaymentDto> PaymentMethods { get; init; }
}

/// <summary>One day of the report.</summary>
public sealed record SalesReportDayDto
{
    /// <summary>The local day, <c>yyyy-MM-dd</c>.</summary>
    public required string Date { get; init; }

    public required decimal Sales { get; init; }

    public required int Orders { get; init; }
}

/// <summary>One of the best-selling products.</summary>
public sealed record SalesReportProductDto
{
    public required int ProductId { get; init; }

    public required string Name { get; init; }

    public required decimal QuantitySold { get; init; }

    public required decimal Revenue { get; init; }

    public required decimal AveragePrice { get; init; }
}

/// <summary>Sales of one category.</summary>
public sealed record SalesReportCategoryDto
{
    public required int CategoryId { get; init; }

    public required string Name { get; init; }

    public required decimal Sales { get; init; }

    /// <summary>Share of all categorised sales, 0 to 100.</summary>
    public required decimal Percentage { get; init; }
}

/// <summary>Sales of one payment method.</summary>
public sealed record SalesReportPaymentDto
{
    public required int PaymentMethodId { get; init; }

    public required string Name { get; init; }

    public required decimal Sales { get; init; }

    public required int Receipts { get; init; }

    /// <summary>Share of the listed payment methods' sales, 0 to 100.</summary>
    public required decimal Percentage { get; init; }
}
