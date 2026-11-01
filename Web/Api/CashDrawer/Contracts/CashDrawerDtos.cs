using System.ComponentModel.DataAnnotations;
using Web.Api.Session.Contracts;

namespace Web.Api.CashDrawer.Contracts;

/// <summary>
/// The drawer page's first load: whether the signed-in user has an open session and, if so, the session with its running figures.
/// <see cref="Status"/> has the shape of <c>session.drawer</c> in the bootstrap payload, so the shell chip and the till can read either.
/// </summary>
public sealed record CurrentDrawerDto
{
    public required DrawerStatusDto Status { get; init; }

    /// <summary>The open session; null while the drawer is closed.</summary>
    public DrawerSessionDto? Session { get; init; }
}

/// <summary>A drawer session (a shift). Open ones carry the running figures; closed ones what was stored when they were closed.</summary>
public sealed record DrawerSessionDto
{
    public required int Id { get; init; }

    public required int UserId { get; init; }

    public required string CashierName { get; init; }

    public required bool IsOpen { get; init; }

    public required DateTime OpenedAt { get; init; }

    public DateTime? ClosedAt { get; init; }

    public required decimal OpeningCash { get; init; }

    /// <summary>Opening cash + sales - cash drops. For a closed session, the figure it was closed against.</summary>
    public required decimal ExpectedCash { get; init; }

    /// <summary>What the cashier counted at closing; null while open.</summary>
    public decimal? ActualCash { get; init; }

    /// <summary>Actual - expected: positive is a surplus, negative a shortage. Null while open.</summary>
    public decimal? CashDifference { get; init; }

    public required decimal TotalSales { get; init; }

    public required int TotalTransactions { get; init; }

    public required decimal TotalDiscounts { get; init; }

    public required decimal TotalAdjustments { get; init; }

    public required decimal TotalCashDrops { get; init; }

    public string? OpeningNotes { get; init; }

    public string? ClosingNotes { get; init; }

    /// <summary>The session's cash drops, oldest first.</summary>
    public required IReadOnlyList<CashDropDto> CashDrops { get; init; }
}

/// <summary>One line of the session history (the signed-in user's own sessions). Figures are what was stored at closing, so an open session has none.</summary>
public sealed record DrawerHistoryRowDto
{
    public required int Id { get; init; }

    public required bool IsOpen { get; init; }

    public required DateTime OpenedAt { get; init; }

    public DateTime? ClosedAt { get; init; }

    public required decimal OpeningCash { get; init; }

    /// <summary>The count entered at closing.</summary>
    public decimal? ClosingCash { get; init; }

    public decimal? TotalSales { get; init; }

    public int? TotalTransactions { get; init; }

    /// <summary>Actual - expected; positive is a surplus.</summary>
    public decimal? CashDifference { get; init; }
}

public sealed record CashDropDto
{
    public required int Id { get; init; }

    public required decimal Amount { get; init; }

    public required DateTime DroppedAt { get; init; }

    public string? Reason { get; init; }

    public string? Notes { get; init; }
}

/// <summary>Opens the signed-in user's drawer.</summary>
public sealed record OpenDrawerRequest
{
    /// <summary>The starting float counted into the drawer; 0 is allowed.</summary>
    [Range(0, 1_000_000_000)]
    public decimal OpeningCash { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}

/// <summary>Closes a session against what was counted.</summary>
public sealed record CloseDrawerRequest
{
    /// <summary>The cash counted in the drawer; 0 is allowed (everything was dropped).</summary>
    [Range(0, 1_000_000_000)]
    public decimal ActualCash { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}

/// <summary>Takes cash out of an open drawer (to the safe or the bank).</summary>
public sealed record CashDropRequest
{
    [Range(0.01, 1_000_000_000)]
    public decimal Amount { get; init; }

    /// <summary>Free text; the page offers Safe Deposit, Bank Deposit, Excess Cash and Other.</summary>
    [MaxLength(200)]
    public string? Reason { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}

/// <summary>The end-of-day report of a closed session (what the Z report page prints).</summary>
public sealed record ZReportDto
{
    public required int SessionId { get; init; }

    public required string CashierName { get; init; }

    public required DateTime OpenedAt { get; init; }

    public required DateTime ClosedAt { get; init; }

    public required decimal OpeningCash { get; init; }

    public required int TotalTransactions { get; init; }

    public required decimal TotalSales { get; init; }

    public required decimal TotalDiscounts { get; init; }

    public required decimal TotalAdjustments { get; init; }

    /// <summary>Gross sales minus discounts.</summary>
    public required decimal NetSales { get; init; }

    public required decimal CashSales { get; init; }

    public required decimal CardSales { get; init; }

    public required decimal OtherPayments { get; init; }

    public required decimal TotalCashDrops { get; init; }

    public required int CashDropCount { get; init; }

    public required decimal ExpectedCash { get; init; }

    public required decimal ActualCash { get; init; }

    public required decimal CashDifference { get; init; }

    public required IReadOnlyList<CashDropDto> CashDrops { get; init; }

    public required IReadOnlyList<ZReportOrderDto> Orders { get; init; }
}

public sealed record ZReportOrderDto
{
    public required int OrderId { get; init; }

    /// <summary><c>ORD-000123</c>.</summary>
    public required string OrderNumber { get; init; }

    public required DateTime OrderDate { get; init; }

    /// <summary>The customer's name; empty for a walk-in (the page says "Walk-in").</summary>
    public required string CustomerName { get; init; }

    public required decimal TotalAmount { get; init; }

    public required string PaymentMethod { get; init; }
}

/// <summary>Outcome of sending the Z report to the receipt printer. Printing never fails the request: offer <see cref="PdfUrl"/> when <see cref="Printed"/> is false.</summary>
public sealed record ZReportPrintResultDto
{
    public required bool Printed { get; init; }

    /// <summary>Localized message for the toast.</summary>
    public required string Message { get; init; }

    /// <summary>Relative URL of the PDF version (<c>GET</c>, same cookie).</summary>
    public required string PdfUrl { get; init; }
}
