using System.Globalization;
using System.Text;
using Application.IServices.Sales;
using Core.Discounts;
using Core.Enum;
using Core.IRepositories.Sales;
using Core.Time;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Infrastructure;
using Web.Api.Orders.Contracts;
using Web.Api.Pos;

namespace Web.Api.Orders;

/// <summary>Filters of the orders list besides the shared paging/search query. Dates are LOCAL calendar days (yyyy-MM-dd).</summary>
public sealed class OrderListQuery
{
    /// <summary>0 Pending ... 5 Returned; omitted = every status.</summary>
    [FromQuery(Name = "status")]
    [System.ComponentModel.DataAnnotations.Range(0, 5)]
    public int? Status { get; set; }

    /// <summary>Only the orders of this customer (the customer page's purchase history).</summary>
    [FromQuery(Name = "customerId")]
    public int? CustomerId { get; set; }

    [FromQuery(Name = "from")]
    public DateOnly? From { get; set; }

    [FromQuery(Name = "to")]
    public DateOnly? To { get; set; }
}

/// <summary>What the orders API needs: list, tiles, one order, status change (cancel = full sale reversal), CSV. Business rules stay in the Application services.</summary>
public interface IOrderQueryService
{
    Task<PagedResult<OrderListItemDto>> ListAsync(PagedQuery query, OrderListQuery filter);

    Task<OrderSummaryDto> SummaryAsync();

    /// <summary>Throws <see cref="PosRuleException"/> 404 <c>order_not_found</c>.</summary>
    Task<OrderDetailDto> GetAsync(int id);

    /// <summary>Throws <see cref="PosRuleException"/>: 404 <c>order_not_found</c>, 409 <c>order_closed</c> / <c>order_cancelled</c>.</summary>
    Task<OrderStatusResultDto> UpdateStatusAsync(int id, int status, int userId);

    /// <summary>The filtered list (every page) as a CSV that opens in Excel.</summary>
    Task<byte[]> ExportAsync(PagedQuery query, OrderListQuery filter);
}

public sealed class OrderQueryService : IOrderQueryService
{
    public const int SummaryDays = 30;

    private readonly IOrderRepository _orderRepo;
    private readonly IOrderService _orders;
    private readonly IReceiptRepository _receipts;
    private readonly ISaleReversalService _reversal;

    public OrderQueryService(IOrderRepository orderRepo, IOrderService orders, IReceiptRepository receipts, ISaleReversalService reversal)
    {
        _orderRepo = orderRepo;
        _orders = orders;
        _receipts = receipts;
        _reversal = reversal;
    }

    public async Task<PagedResult<OrderListItemDto>> ListAsync(PagedQuery query, OrderListQuery filter)
    {
        var all = await FilteredAsync(query, filter);
        var page = all.Skip(query.Skip).Take(query.PageSize).Select(ToItem).ToList();
        return PagedResult<OrderListItemDto>.Of(page, all.Count, query);
    }

    public async Task<OrderSummaryDto> SummaryAsync()
    {
        var now = DateTime.UtcNow;
        var rows = await _orderRepo.GetListRowsAsync(new OrderListFilter(FromUtc: now.AddDays(-SummaryDays), ToUtc: now));

        var cancelled = (int)OrderStatus.Cancelled;
        return new OrderSummaryDto
        {
            PeriodDays = SummaryDays,
            TotalOrders = rows.Count,
            DeliveredOrders = rows.Count(r => r.Status == (int)OrderStatus.Delivered),
            PendingOrders = rows.Count(r => r.Status == (int)OrderStatus.Pending),
            ProcessingOrders = rows.Count(r => r.Status == (int)OrderStatus.Processing),
            CancelledOrders = rows.Count(r => r.Status == cancelled),
            Revenue = rows
                .Where(r => r.Status != cancelled)
                .GroupBy(r => new { r.CurrencyId, r.CurrencyNotation })
                .Select(g => new OrderRevenueDto { CurrencyId = g.Key.CurrencyId, CurrencyNotation = g.Key.CurrencyNotation, Amount = g.Sum(r => r.TotalAmount) })
                .OrderByDescending(r => r.Amount)
                .ToList(),
        };
    }

    public async Task<OrderDetailDto> GetAsync(int id)
    {
        var order = await FindAsync(id);
        var lines = (order.Cart?.CartItems ?? [])
            .Select(ci => new OrderLineDto
            {
                ProductId = ci.ProductId,
                ProductName = ci.Product?.Name ?? "",
                Barcode = ci.Product?.Barcode ?? "",
                Quantity = ci.Quantity,
                WeightOrVolume = ci.WeightOrVolume,
                UnitPrice = ci.UnitPrice,
                TotalPrice = ci.TotalPrice,
            })
            .ToList();

        var subtotal = lines.Sum(l => l.TotalPrice);
        var discount = DiscountBreakdown.From(subtotal, order.Cart?.DiscountAmount, order.Cart?.DiscountPercentage);
        var adjustment = order.TotalAmount - (subtotal - discount.Amount);

        var receipts = await _receipts.GetByOrderIdAsync(id);

        return new OrderDetailDto
        {
            Id = order.Id,
            OrderNumber = NumberOf(order.Id),
            CustomerId = order.Customer != null ? order.CustomerId : null,
            CustomerName = order.Customer != null ? $"{order.Customer.FirstName} {order.Customer.LastName}".Trim() : null,
            OrderDate = Utc(order.OrderDate),
            CreatedAt = Utc(order.CreatedAt),
            UpdatedAt = order.UpdatedAt is { } updated ? Utc(updated) : null,
            Status = order.Status,
            CurrencyId = order.CurrencyId,
            CurrencyCode = order.Currency?.Code ?? "",
            CurrencyNotation = order.Currency?.Notation ?? "",
            Subtotal = subtotal,
            DiscountAmount = discount.Amount,
            DiscountPercentage = discount.Percentage,
            // Sub-cent differences are rounding noise, not an adjustment (same rule as Receipt.GetAdjustment).
            Adjustment = Math.Round(adjustment, 2) == 0 ? 0 : adjustment,
            TaxAmount = 0,
            TotalAmount = order.TotalAmount,
            Lines = lines,
            Receipts = receipts.Select(r => new OrderReceiptRefDto { Id = r.Id, ReceiptNumber = r.ReceiptNumber, IsVoid = r.IsVoid }).ToList(),
        };
    }

    public async Task<OrderStatusResultDto> UpdateStatusAsync(int id, int status, int userId)
    {
        var order = await FindAsync(id);
        var number = NumberOf(order.Id);

        if (status == (int)OrderStatus.Cancelled)
        {
            // Cancelling reverses the whole sale: receipt voided, stock back, debt and drawer adjusted - all or nothing.
            try
            {
                var result = await _reversal.CancelOrderAsync(id, userId, "Cancelled from the Orders page");
                return new OrderStatusResultDto
                {
                    OrderId = id,
                    OrderNumber = result.OrderNumber,
                    Status = (int)OrderStatus.Cancelled,
                    Reversal = new SaleReversalDto
                    {
                        OrderCancelled = result.OrderCancelled,
                        ReceiptsVoided = result.ReceiptsVoided,
                        StockLinesRestored = result.StockLinesRestored,
                        DebtReversed = result.DebtReversed,
                    },
                };
            }
            catch (InvalidOperationException)
            {
                throw new PosRuleException(StatusCodes.Status409Conflict, SalesCodes.OrderClosed, "Sales_OrderClosed", null, number);
            }
        }

        try
        {
            var updated = await _orders.UpdateOrderStatusAsync(id, status);
            return new OrderStatusResultDto { OrderId = id, OrderNumber = number, Status = updated.Status };
        }
        catch (InvalidOperationException)
        {
            // The only refusal of UpdateOrderStatusAsync left here: a cancelled order cannot be reopened (its stock went back on the shelf).
            throw new PosRuleException(StatusCodes.Status409Conflict, SalesCodes.OrderCancelled, "Sales_OrderCancelled", null, number);
        }
    }

    public async Task<byte[]> ExportAsync(PagedQuery query, OrderListQuery filter)
    {
        var rows = await FilteredAsync(query, filter);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", new[] { "Order", "Date", "Customer", "Status", "Items", "Total", "Currency" }.Select(Cell)));
        foreach (var r in rows)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                NumberOf(r.Id),
                r.OrderDate.UtcToLocal().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                r.CustomerName ?? "Walk-in",
                ((OrderStatus)r.Status).ToString(),
                r.ItemCount.ToString(CultureInfo.InvariantCulture),
                r.TotalAmount.ToString("F2", CultureInfo.InvariantCulture),
                r.CurrencyNotation,
            }.Select(Cell)));
        }

        // The BOM makes Excel read the file as UTF-8, so Arabic customer names survive.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    /// <summary>The filtered, searched and sorted rows (every page).</summary>
    private async Task<List<OrderListRow>> FilteredAsync(PagedQuery query, OrderListQuery filter)
    {
        var (fromUtc, toUtc) = SalesDates.ToUtcRange(filter.From, filter.To);
        var rows = await _orderRepo.GetListRowsAsync(new OrderListFilter(fromUtc, toUtc, filter.Status, filter.CustomerId));

        var term = query.Q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // The old search: the id, the customer's name or the total, case-insensitive. "ORD-000042" finds order 42 too.
            var digits = term.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase) ? term[4..].TrimStart('0') : term;
            rows = rows.Where(r =>
                (digits.Length > 0 && r.Id.ToString(CultureInfo.InvariantCulture).Contains(digits, StringComparison.Ordinal)) ||
                (r.CustomerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                r.TotalAmount.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // No sort asked for = newest first, as the old list.
        var desc = query.Sort is null || query.Descending;
        IOrderedEnumerable<OrderListRow> sorted = (query.Sort?.ToLowerInvariant()) switch
        {
            "number" => Order(rows, r => r.Id, desc),
            "customer" => Order(rows, r => r.CustomerName ?? "", desc),
            "total" => Order(rows, r => r.TotalAmount, desc),
            "status" => Order(rows, r => r.Status, desc),
            "items" => Order(rows, r => r.ItemCount, desc),
            _ => Order(rows, r => r.OrderDate, desc),
        };
        return sorted.ThenByDescending(r => r.Id).ToList();
    }

    private static IOrderedEnumerable<OrderListRow> Order<TKey>(IEnumerable<OrderListRow> rows, Func<OrderListRow, TKey> key, bool descending)
        => descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

    private static OrderListItemDto ToItem(OrderListRow r) => new()
    {
        Id = r.Id,
        OrderNumber = NumberOf(r.Id),
        CustomerId = r.CustomerName != null ? r.CustomerId : null,
        CustomerName = r.CustomerName,
        OrderDate = Utc(r.OrderDate),
        TotalAmount = r.TotalAmount,
        CurrencyId = r.CurrencyId,
        CurrencyNotation = r.CurrencyNotation,
        Status = r.Status,
        ItemCount = r.ItemCount,
    };

    private async Task<Core.Entities.Sales.Order> FindAsync(int id)
    {
        try { return await _orders.GetByIdAsync(id); }
        catch (ArgumentNullException) { throw new PosRuleException(StatusCodes.Status404NotFound, PosCodes.OrderNotFound, "Pos_OrderNotFound"); }
    }

    internal static string NumberOf(int id) => $"ORD-{id:D6}";

    /// <summary>The database hands DateTimes back as Unspecified; the wire format wants the Z.</summary>
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>One CSV cell: quoted, and neutralised when a spreadsheet would read it as a formula (a customer can be named "=cmd|...").</summary>
    internal static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

/// <summary>Codes of the sales back office on top of the shared ones in <c>ApiCodes</c> and <see cref="PosCodes"/>.</summary>
public static class SalesCodes
{
    public const string ReceiptNotFound = "receipt_not_found";

    /// <summary>The order was already cancelled or returned: there is no sale left to cancel.</summary>
    public const string OrderClosed = "order_closed";

    /// <summary>A cancelled order cannot be reopened (its stock went back on the shelf).</summary>
    public const string OrderCancelled = "order_cancelled";

    public const string ReceiptAlreadyVoid = "receipt_already_void";
}

/// <summary>Picked LOCAL calendar days to UTC bounds for comparing with stored timestamps.</summary>
internal static class SalesDates
{
    public static (DateTime? FromUtc, DateTime? ToUtc) ToUtcRange(DateOnly? from, DateOnly? to)
    {
        DateTime? fromUtc = from is { } f ? AppTime.LocalDayStartUtc(f.ToDateTime(TimeOnly.MinValue)) : null;
        DateTime? toUtc = to is { } t ? AppTime.LocalDayStartUtc(t.ToDateTime(TimeOnly.MinValue).AddDays(1)).AddTicks(-1) : null;
        return (fromUtc, toUtc);
    }
}
