using System.Globalization;
using Application.IServices.Sales;
using Core.Auditing;
using Core.Entities.Logging;
using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Core.Printing;
using Core.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Orders;
using Web.Api.Orders.Contracts;
using Web.Api.Pos;
using Web.Api.Receipts.Contracts;

namespace Web.Api.Receipts;

/// <summary>Filters of the receipts list besides the shared paging/search query. Dates are LOCAL calendar days (yyyy-MM-dd).</summary>
public sealed class ReceiptListQuery
{
    /// <summary><c>valid</c> or <c>void</c>; anything else = both.</summary>
    [FromQuery(Name = "status")]
    public string? Status { get; set; }

    [FromQuery(Name = "from")]
    public DateOnly? From { get; set; }

    [FromQuery(Name = "to")]
    public DateOnly? To { get; set; }
}

/// <summary>What the receipts API needs: list, stats, one receipt, void (a full sale reversal), reprint and PDF.</summary>
public interface IReceiptQueryService
{
    Task<PagedResult<ReceiptListItemDto>> ListAsync(PagedQuery query, ReceiptListQuery filter);

    Task<ReceiptStatsDto> StatsAsync(DateOnly? from, DateOnly? to);

    /// <summary>Throws <see cref="PosRuleException"/> 404 <c>receipt_not_found</c>.</summary>
    Task<ReceiptDetailDto> GetAsync(int id);

    /// <summary>Throws <see cref="PosRuleException"/>: 404 <c>receipt_not_found</c>, 409 <c>receipt_already_void</c>.</summary>
    Task<SaleReversalDto> VoidAsync(int id, string reason, int userId);

    /// <summary>Changes the customer of the sale behind the receipt. Throws <see cref="PosRuleException"/>: 404 <c>receipt_not_found</c>, 409 <c>receipt_already_void</c> / <c>order_closed</c> / <c>customer_not_found</c>, 400 <c>debt_requires_customer</c>.</summary>
    Task<SaleCustomerResultDto> ChangeCustomerAsync(int id, int? customerId, int userId);

    Task<ReceiptPrintResultDto> PrintAsync(int id);

    Task<(byte[] Pdf, string FileName)> GetPdfAsync(int id);
}

public sealed class ReceiptQueryService : IReceiptQueryService
{
    public const int StatsDays = 30;

    private readonly IReceiptRepository _receiptRepo;
    private readonly IReceiptService _receipts;
    private readonly ISaleReversalService _reversal;
    private readonly ISaleCustomerService _saleCustomer;
    private readonly IThermalPrinterService _printer;
    private readonly IAuditTrail _audit;
    private readonly IStringLocalizer<SharedResource> _text;

    public ReceiptQueryService(
        IReceiptRepository receiptRepo, IReceiptService receipts, ISaleReversalService reversal, ISaleCustomerService saleCustomer,
        IThermalPrinterService printer, IAuditTrail audit, IStringLocalizer<SharedResource> text)
    {
        _receiptRepo = receiptRepo;
        _receipts = receipts;
        _reversal = reversal;
        _saleCustomer = saleCustomer;
        _printer = printer;
        _audit = audit;
        _text = text;
    }

    public async Task<PagedResult<ReceiptListItemDto>> ListAsync(PagedQuery query, ReceiptListQuery filter)
    {
        var (fromUtc, toUtc) = SalesDates.ToUtcRange(filter.From, filter.To);
        bool? isVoid = filter.Status?.ToLowerInvariant() switch { "valid" => false, "void" => true, _ => null };
        var rows = await _receiptRepo.GetListRowsAsync(new ReceiptListFilter(fromUtc, toUtc, isVoid));

        // The old search: receipt number, customer name or total; the phone is added because the old "search receipts" box promised it.
        var term = query.Q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            rows = rows.Where(r =>
                r.ReceiptNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.CustomerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.CustomerPhone?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                r.TotalAmount.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // No sort asked for = newest first. The void tab sorts by when the receipt was voided.
        var desc = query.Sort is null || query.Descending;
        IEnumerable<ReceiptListRow> sorted = (query.Sort?.ToLowerInvariant()) switch
        {
            "number" => Order(rows, r => r.ReceiptNumber, desc),
            "customer" => Order(rows, r => r.CustomerName ?? "", desc),
            "cashier" => Order(rows, r => r.CashierName, desc),
            "amount" => Order(rows, r => r.TotalAmount, desc),
            "payment" => Order(rows, r => r.PaymentMethod, desc),
            "voidDate" or "voiddate" => Order(rows, r => r.VoidDate ?? r.ReceiptDate, desc),
            _ => Order(rows, r => r.ReceiptDate, desc),
        };

        var ordered = sorted.ToList();
        var page = ordered.Skip(query.Skip).Take(query.PageSize).Select(ToItem).ToList();
        return PagedResult<ReceiptListItemDto>.Of(page, ordered.Count, query);
    }

    public async Task<ReceiptStatsDto> StatsAsync(DateOnly? from, DateOnly? to)
    {
        // No range = the last 30 days up to today (local), as the old stats call.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var start = from ?? to?.AddDays(-StatsDays) ?? today.AddDays(-StatsDays);
        var end = to ?? today;
        var (fromUtc, toUtc) = SalesDates.ToUtcRange(start, end);

        var rows = await _receiptRepo.GetListRowsAsync(new ReceiptListFilter(fromUtc, toUtc));
        var valid = rows.Where(r => !r.IsVoid).ToList();

        return new ReceiptStatsDto
        {
            From = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ValidCount = valid.Count,
            VoidCount = rows.Count - valid.Count,
            Currencies = valid
                .GroupBy(r => new { r.CurrencyId, r.CurrencyNotation })
                .Select(g => new ReceiptCurrencyStatsDto
                {
                    CurrencyId = g.Key.CurrencyId,
                    CurrencyNotation = g.Key.CurrencyNotation,
                    TotalSales = g.Sum(r => r.TotalAmount),
                    ReceiptCount = g.Count(),
                    AverageReceipt = g.Average(r => r.TotalAmount),
                    Daily = g
                        .GroupBy(r => r.ReceiptDate.UtcToLocal().Date)
                        .OrderBy(d => d.Key)
                        .Select(d => new ReceiptDaySalesDto { Date = d.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Amount = d.Sum(r => r.TotalAmount) })
                        .ToList(),
                })
                .OrderByDescending(c => c.TotalSales)
                .ToList(),
            PaymentMethods = valid
                .GroupBy(r => r.PaymentMethod)
                .Select(g => new ReceiptPaymentShareDto { PaymentMethod = g.Key, Count = g.Count() })
                .OrderByDescending(p => p.Count)
                .ThenBy(p => p.PaymentMethod, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
        };
    }

    public async Task<ReceiptDetailDto> GetAsync(int id)
    {
        var r = await FindAsync(id);
        var discount = r.GetDiscount();

        string? Name(Core.Entities.Users.User? user)
        {
            var name = $"{user?.FirstName} {user?.LastName}".Trim();
            return name.Length == 0 ? null : name;
        }

        var customer = r.Customer != null ? $"{r.Customer.FirstName} {r.Customer.LastName}".Trim() : null;

        return new ReceiptDetailDto
        {
            Id = r.Id,
            ReceiptNumber = r.ReceiptNumber,
            OrderId = r.OrderId,
            ReceiptDate = Utc(r.ReceiptDate),
            CustomerId = r.Customer != null ? r.CustomerId : null,
            CustomerName = !string.IsNullOrEmpty(r.CustomerName) ? r.CustomerName : (string.IsNullOrEmpty(customer) ? null : customer),
            CustomerPhone = string.IsNullOrEmpty(r.CustomerPhone) ? null : r.CustomerPhone,
            CashierName = Name(r.Cashier) ?? "",
            PaymentMethod = r.PaymentMethod?.Name ?? "",
            CurrencyCode = r.Currency?.Code ?? "",
            CurrencyNotation = r.Currency?.Notation ?? "",
            ExchangeRate = r.ExchangeRate,
            SubTotal = r.SubTotal,
            DiscountAmount = r.DiscountAmount,
            DiscountPercentage = discount.Percentage,
            TaxAmount = r.TaxAmount,
            Adjustment = r.GetAdjustment(),
            TotalAmount = r.TotalAmount,
            AmountPaid = r.AmountPaid,
            ChangeAmount = r.ChangeAmount,
            IsFullyPaid = r.IsFullyPaid(),
            Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes,
            IsVoid = r.IsVoid,
            VoidDate = r.VoidDate is { } voided ? Utc(voided) : null,
            VoidReason = r.VoidReason,
            VoidedBy = r.IsVoid ? Name(r.VoidedByUser) : null,
            CreatedAt = Utc(r.CreatedAt),
            PrintedBy = r.PrintedBy,
            PrintedAt = r.PrintedAt is { } printed ? Utc(printed) : null,
            Lines = (r.ReceiptItems ?? []).Where(i => !i.IsDeleted)
                .Select(i => new ReceiptLineDto
                {
                    ProductName = i.ProductName,
                    Barcode = string.IsNullOrEmpty(i.ProductBarcode) ? null : i.ProductBarcode,
                    Quantity = i.Quantity,
                    Unit = string.IsNullOrEmpty(i.Unit) ? null : i.Unit,
                    UnitPrice = i.UnitPrice,
                    DiscountAmount = i.DiscountAmount,
                    TaxAmount = i.TaxAmount,
                    LineTotal = i.LineTotal,
                })
                .ToList(),
            Payments = (r.ReceiptPayments ?? []).Where(p => !p.IsDeleted)
                .Select(p => new ReceiptPaymentDto
                {
                    PaymentMethod = p.PaymentMethod?.Name ?? "",
                    ReferenceNumber = string.IsNullOrEmpty(p.ReferenceNumber) ? null : p.ReferenceNumber,
                    Amount = p.Amount,
                    PaymentDate = Utc(p.PaymentDate),
                })
                .ToList(),
        };
    }

    public async Task<SaleReversalDto> VoidAsync(int id, string reason, int userId)
    {
        await FindAsync(id); // a clean 404 before the reversal's own exceptions
        try
        {
            // Voids the receipt AND reverses the sale behind it (order cancelled, stock back, debt/drawer adjusted).
            var result = await _reversal.VoidReceiptAsync(id, userId, reason);
            return new SaleReversalDto
            {
                OrderCancelled = result.OrderCancelled,
                ReceiptsVoided = result.ReceiptsVoided,
                StockLinesRestored = result.StockLinesRestored,
                DebtReversed = result.DebtReversed,
            };
        }
        catch (InvalidOperationException)
        {
            // The service refuses only a receipt that is already void.
            var receipt = await FindAsync(id);
            throw new PosRuleException(StatusCodes.Status409Conflict, SalesCodes.ReceiptAlreadyVoid, "Sales_ReceiptAlreadyVoid", null, receipt.ReceiptNumber);
        }
    }

    public async Task<SaleCustomerResultDto> ChangeCustomerAsync(int id, int? customerId, int userId)
    {
        var receipt = await FindAsync(id);
        if (receipt.IsVoid)
            throw new PosRuleException(StatusCodes.Status409Conflict, SalesCodes.ReceiptAlreadyVoid, "Sales_ReceiptAlreadyVoid", null, receipt.ReceiptNumber);

        return await SaleCustomer.ChangeAsync(_saleCustomer, receipt.OrderId, customerId, userId);
    }

    public async Task<ReceiptPrintResultDto> PrintAsync(int id)
    {
        var receipt = await FindAsync(id);
        var document = await _printer.GetReceiptDocumentAsync(receipt);

        // A reprint: straight to the receipt printer, never a new receipt row. A printer problem is an answer, not an error.
        var printed = await _printer.PrintDocumentAsync(document, jobName: document.Title);
        _audit.LogBusiness(
            printed ? AuditActions.Print : AuditActions.PrintFailed,
            printed ? $"Sent '{document.Title}' to the receipt printer" : $"Could not print '{document.Title}' - receipt printer unavailable",
            "PrintJob",
            printed ? AuditOutcome.Success : AuditOutcome.Failure,
            printed ? AuditLogSeverity.Info : AuditLogSeverity.Warning,
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, receiptId = id });

        return new ReceiptPrintResultDto
        {
            Printed = printed,
            Message = _text[printed ? "Pos_PrintOk" : "Pos_PrintFailed"].Value,
            PdfUrl = $"/api/v1/receipts/{id}/pdf",
        };
    }

    public async Task<(byte[] Pdf, string FileName)> GetPdfAsync(int id)
    {
        var receipt = await FindAsync(id);
        var document = await _printer.GetReceiptDocumentAsync(receipt);
        var pdf = _printer.GeneratePdf(document, PdfPaper.Receipt);

        _audit.LogBusiness(AuditActions.PdfGenerated, $"PDF generated: '{document.Title}'", "PrintJob",
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, paper = nameof(PdfPaper.Receipt), bytes = pdf.Length });
        return (pdf, $"{document.FileName}.pdf");
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private async Task<Receipt> FindAsync(int id)
    {
        try { return await _receipts.GetReceiptAsync(id); }
        catch (ArgumentNullException) { throw new PosRuleException(StatusCodes.Status404NotFound, SalesCodes.ReceiptNotFound, "Sales_ReceiptNotFound"); }
    }

    private static IEnumerable<ReceiptListRow> Order<TKey>(IEnumerable<ReceiptListRow> rows, Func<ReceiptListRow, TKey> key, bool descending)
        => (descending ? rows.OrderByDescending(key) : rows.OrderBy(key)).ThenByDescending(r => r.Id);

    private static ReceiptListItemDto ToItem(ReceiptListRow r) => new()
    {
        Id = r.Id,
        ReceiptNumber = r.ReceiptNumber,
        OrderId = r.OrderId,
        ReceiptDate = Utc(r.ReceiptDate),
        CustomerName = r.CustomerName,
        CashierName = r.CashierName,
        PaymentMethod = r.PaymentMethod,
        TotalAmount = r.TotalAmount,
        CurrencyNotation = r.CurrencyNotation,
        IsVoid = r.IsVoid,
        VoidDate = r.VoidDate is { } voided ? Utc(voided) : null,
        VoidReason = r.VoidReason,
        VoidedBy = r.VoidedBy,
    };

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
