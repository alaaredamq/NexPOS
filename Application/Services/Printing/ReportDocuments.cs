using Core.Time;
using System.Globalization;
using Application.IServices.Sales;
using Core.Printing;

namespace Application.Services.Printing;

public record LowStockItem(string Name, string Category, decimal Stock, decimal Minimum);

public record StockValueItem(string Name, string Category, decimal Quantity, string UnitCost, string Value);

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
