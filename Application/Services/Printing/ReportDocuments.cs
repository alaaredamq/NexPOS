using Core.Time;
using System.Globalization;
using Application.IServices.Sales;
using Core.Printing;

namespace Application.Services.Printing;

public record LowStockItem(string Name, string Category, decimal Stock, decimal Minimum);

/// <summary>A titled block of label / value rows (money already formatted by the caller).</summary>
public record ReportSection(string Title, IReadOnlyList<(string Label, string Value)> Rows);

public record StockValueItem(string Name, string Category, decimal Quantity, string UnitCost, string Value);

/// <summary>One product line of a purchase receipt; everything already formatted.</summary>
public record PurchaseReceiptLine(string Name, string Quantity, string UnitCost, string Total);

/// <summary>
/// Report pages as printable lines, so a report can go to the receipt printer or be saved as a
/// PDF from one definition. Receipt-paper reports are laid out as lists (a name line followed by
/// its figures) because 80mm paper has no room for table columns.
/// </summary>
public static class ReportDocuments
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string Money(decimal amount) => $"${amount.ToString("F2", Inv)}";

    private static string Signed(decimal amount) =>
        amount < 0 ? $"-${Math.Abs(amount).ToString("F2", Inv)}" : $"+${amount.ToString("F2", Inv)}";

    public static PrintDocument ZReport(ZReport report)
    {
        var lines = new List<PrintLine>
        {
            PrintLine.Header("Z-REPORT"),
            PrintLine.Centered("End of Day Summary"),
            PrintLine.Centered($"Session #{report.SessionId}"),
            PrintLine.Separator(),
            PrintLine.Bold("SESSION INFORMATION"),
            PrintLine.Row("Cashier:", report.CashierName),
            PrintLine.Row("Opened:", report.OpenedAt.UtcToLocal().ToString("yyyy-MM-dd HH:mm", Inv)),
            PrintLine.Row("Closed:", report.ClosedAt.UtcToLocal().ToString("yyyy-MM-dd HH:mm", Inv)),
            PrintLine.Row("Duration:", $"{(report.ClosedAt - report.OpenedAt).TotalHours.ToString("F1", Inv)} hours"),
            PrintLine.Separator(),
            PrintLine.Bold("SALES SUMMARY"),
            PrintLine.Row("Transactions:", report.TotalTransactions.ToString(Inv)),
            PrintLine.Row("Gross Sales:", Money(report.TotalSales)),
            PrintLine.Row("Discounts:", $"-{Money(report.TotalDiscounts)}"),
            PrintLine.Row("Adjustments:", Signed(report.TotalAdjustments)),
            PrintLine.Row("Net Sales:", Money(report.NetSales), bold: true),
            PrintLine.Separator(),
            PrintLine.Bold("PAYMENT METHODS"),
            PrintLine.Row("Cash:", Money(report.CashSales)),
            PrintLine.Row("Card:", Money(report.CardSales)),
            PrintLine.Row("Other:", Money(report.OtherPayments)),
            PrintLine.Separator(),
            PrintLine.Bold("CASH RECONCILIATION"),
            PrintLine.Row("Opening Cash:", Money(report.OpeningCash)),
            PrintLine.Row("Cash Sales:", $"+{Money(report.CashSales)}"),
            PrintLine.Row($"Cash Drops ({report.CashDropCount}):", $"-{Money(report.TotalCashDrops)}"),
            PrintLine.Row("Expected Cash:", Money(report.ExpectedCash), bold: true),
            PrintLine.Row("Actual Cash:", Money(report.ActualCash), bold: true),
            report.CashDifference == 0
                ? PrintLine.Centered("PERFECT BALANCE")
                : report.CashDifference > 0
                    ? PrintLine.Row("OVERAGE:", Signed(report.CashDifference), bold: true)
                    : PrintLine.Row("SHORTAGE:", Signed(report.CashDifference), bold: true),
        };

        if (report.CashDrops.Count > 0)
        {
            lines.Add(PrintLine.Separator());
            lines.Add(PrintLine.Bold("CASH DROPS"));
            foreach (var drop in report.CashDrops)
            {
                lines.Add(PrintLine.Row($"  {drop.DroppedAt.UtcToLocal().ToString("HH:mm", Inv)} {drop.Reason ?? "-"}", Money(drop.Amount)));
                if (!string.IsNullOrWhiteSpace(drop.Notes))
                    lines.Add(PrintLine.Plain($"    {drop.Notes}"));
            }
        }

        if (report.Orders.Count > 0)
        {
            lines.Add(PrintLine.Separator());
            lines.Add(PrintLine.Bold("TRANSACTIONS"));
            foreach (var order in report.Orders)
            {
                lines.Add(PrintLine.Row($"  {order.OrderNumber} {order.OrderDate.UtcToLocal().ToString("HH:mm", Inv)}", Money(order.TotalAmount)));
                var detail = string.Join(" - ", new[] { order.CustomerName, order.PaymentMethod }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (detail.Length > 0)
                    lines.Add(PrintLine.Plain($"    {detail}"));
            }
        }

        lines.Add(PrintLine.Spacer());
        lines.Add(PrintLine.Centered($"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)}"));
        lines.Add(PrintLine.Centered("Cash drawer reconciliation report"));

        return new PrintDocument($"Z-Report-Session-{report.SessionId}", lines);
    }

    public static PrintDocument LowStock(IReadOnlyCollection<LowStockItem> items)
    {
        var lines = new List<PrintLine>
        {
            PrintLine.Header("LOW STOCK"),
            PrintLine.Centered(DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)),
            PrintLine.Row("Products:", items.Count.ToString(Inv)),
            PrintLine.Row("Out of stock:", items.Count(i => i.Stock <= 0).ToString(Inv)),
            PrintLine.Separator(),
        };

        foreach (var item in items)
        {
            lines.Add(PrintLine.Bold(item.Name));
            lines.Add(PrintLine.Row($"  {item.Category}", $"{item.Stock.ToString("0.###", Inv)} / min {item.Minimum.ToString("0.###", Inv)}"));
        }

        if (items.Count == 0)
            lines.Add(PrintLine.Centered("All products have enough stock"));

        return new PrintDocument("Low-Stock-Report", lines);
    }

    /// <summary>The sales report of a range of days: the totals, then one block per breakdown. The caller formats the figures.</summary>
    public static PrintDocument SalesReport(string from, string to, IReadOnlyList<(string Label, string Value)> totals, IReadOnlyList<ReportSection> sections)
    {
        var lines = new List<PrintLine>
        {
            PrintLine.Header("SALES REPORT"),
            PrintLine.Centered(from == to ? from : $"{from} - {to}"),
            PrintLine.Separator(),
        };
        lines.AddRange(totals.Select(t => PrintLine.Row(t.Label + ":", t.Value, bold: true)));

        foreach (var section in sections.Where(s => s.Rows.Count > 0))
        {
            lines.Add(PrintLine.Separator());
            lines.Add(PrintLine.Bold(section.Title));
            lines.AddRange(section.Rows.Select(r => PrintLine.Row(r.Label, r.Value)));
        }

        lines.Add(PrintLine.Spacer());
        lines.Add(PrintLine.Centered($"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)}"));
        return new PrintDocument($"Sales-Report-{from}-{to}", lines);
    }

    /// <summary>
    /// The paper that goes with a purchase: who it was bought from, what came in, what it cost and what is still owed.
    /// Quantities and money are formatted by the caller.
    /// </summary>
    public static PrintDocument PurchaseReceipt(
        string storeName,
        IReadOnlyList<string> storeLines,
        string number,
        string date,
        string supplier,
        string? supplierReference,
        string? enteredBy,
        IReadOnlyList<PurchaseReceiptLine> items,
        IReadOnlyList<(string Label, string Value)> totals,
        string? notes,
        string? cancelledNote)
    {
        var lines = new List<PrintLine> { PrintLine.Header(storeName.ToUpperInvariant()) };
        lines.AddRange(storeLines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(PrintLine.Centered));
        lines.Add(PrintLine.Separator());
        lines.Add(PrintLine.Header("PURCHASE RECEIPT"));
        if (cancelledNote is not null)
            lines.Add(PrintLine.Header("*** CANCELLED ***"));
        lines.Add(PrintLine.Row("No:", number, bold: true));
        lines.Add(PrintLine.Row("Date:", date));
        lines.Add(PrintLine.Row("Supplier:", supplier));
        if (!string.IsNullOrWhiteSpace(supplierReference))
            lines.Add(PrintLine.Row("Supplier ref:", supplierReference));
        if (!string.IsNullOrWhiteSpace(enteredBy))
            lines.Add(PrintLine.Row("Received by:", enteredBy));
        lines.Add(PrintLine.Separator());

        foreach (var item in items)
        {
            lines.Add(PrintLine.Bold(item.Name));
            lines.Add(PrintLine.Row($"  {item.Quantity} x {item.UnitCost}", item.Total));
        }

        lines.Add(PrintLine.Separator());
        lines.AddRange(totals.Select((t, i) => PrintLine.Row(t.Label + ":", t.Value, bold: i == totals.Count - 1 || t.Label == "TOTAL")));

        if (!string.IsNullOrWhiteSpace(notes))
        {
            lines.Add(PrintLine.Separator());
            lines.Add(PrintLine.Bold("NOTES"));
            lines.Add(PrintLine.Plain(notes));
        }

        if (cancelledNote is not null)
        {
            lines.Add(PrintLine.Separator());
            lines.Add(PrintLine.Plain(cancelledNote));
        }

        lines.Add(PrintLine.Spacer());
        lines.Add(PrintLine.Plain("Received by: ______________________"));
        lines.Add(PrintLine.Spacer());
        lines.Add(PrintLine.Plain("Supplier:    ______________________"));
        lines.Add(PrintLine.Spacer());
        lines.Add(PrintLine.Centered($"Printed {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)}"));
        return new PrintDocument($"Purchase-{number}", lines);
    }

    public static PrintDocument StockValue(IReadOnlyCollection<StockValueItem> items, string totalValue)
    {
        var lines = new List<PrintLine>
        {
            PrintLine.Header("STOCK VALUE"),
            PrintLine.Centered(DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)),
            PrintLine.Row("Products:", items.Count.ToString(Inv)),
            PrintLine.Row("Total value:", totalValue, bold: true),
            PrintLine.Separator(),
        };

        foreach (var item in items)
        {
            lines.Add(PrintLine.Bold(item.Name));
            lines.Add(PrintLine.Row($"  {item.Quantity.ToString("0.###", Inv)} x {item.UnitCost}", item.Value));
        }

        return new PrintDocument("Stock-Value-Report", lines);
    }
}
