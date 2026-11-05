using System.Globalization;
using System.Text;
using Application.IServices.Purchasing;
using Application.IServices.Sales;
using Application.IServices.Settings;
using Application.Services.Printing;
using Core.Auditing;
using Core.Entities.Logging;
using Core.Entities.Purchasing;
using Core.Enum;
using Core.Printing;
using Core.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Purchases.Contracts;
using static Web.Helpers.CurrencyHelper;

namespace Web.Api.Purchases;

/// <summary>The list filter: <c>q</c> matches the number, the supplier's name and the supplier's own reference.</summary>
public sealed class PurchaseListQuery
{
    [FromQuery(Name = "supplierId")]
    public int? SupplierId { get; set; }

    /// <summary><c>received</c> or <c>cancelled</c>; omitted = both.</summary>
    [FromQuery(Name = "status")]
    [System.ComponentModel.DataAnnotations.RegularExpression("(?i)^(received|cancelled)$")]
    public string? Status { get; set; }

    [FromQuery(Name = "from")]
    public DateOnly? From { get; set; }

    [FromQuery(Name = "to")]
    public DateOnly? To { get; set; }
}

public interface IPurchaseApiService
{
    Task<PagedResult<PurchaseListItemDto>> ListAsync(PagedQuery query, PurchaseListQuery filter);

    Task<PurchaseSummaryDto> SummaryAsync(PagedQuery query, PurchaseListQuery filter);

    /// <summary>Null when there is no such purchase.</summary>
    Task<PurchaseDetailDto?> GetAsync(int id);

    /// <summary>Throws <see cref="PurchaseRuleException"/>.</summary>
    Task<PurchaseDetailDto> CreateAsync(PurchaseRequest request, int userId);

    /// <summary>Null when there is no such purchase. Throws <see cref="PurchaseRuleException"/>.</summary>
    Task<PurchaseDetailDto?> CancelAsync(int id, int userId, string? reason);

    /// <summary>Null when there is no such purchase.</summary>
    Task<PurchasePrintResultDto?> PrintAsync(int id);

    Task<(byte[] Pdf, string FileName)?> PdfAsync(int id);

    Task<byte[]> ExportAsync(PagedQuery query, PurchaseListQuery filter);
}

/// <summary>Maps the purchase service to the API's DTOs and builds the printed receipt.</summary>
public sealed class PurchaseApiService : IPurchaseApiService
{
    private readonly IPurchaseService _purchases;
    private readonly IThermalPrinterService _printer;
    private readonly IStoreSettingsService _store;
    private readonly IAuditTrail _audit;
    private readonly IStringLocalizer<SharedResource> _text;

    public PurchaseApiService(
        IPurchaseService purchases,
        IThermalPrinterService printer,
        IStoreSettingsService store,
        IAuditTrail audit,
        IStringLocalizer<SharedResource> text)
    {
        _purchases = purchases;
        _printer = printer;
        _store = store;
        _audit = audit;
        _text = text;
    }

    public async Task<PagedResult<PurchaseListItemDto>> ListAsync(PagedQuery query, PurchaseListQuery filter)
    {
        var (rows, total) = await _purchases.SearchAsync(ToFilter(query, filter), query.Sort, query.Descending || string.IsNullOrEmpty(query.Sort) && string.IsNullOrEmpty(query.Dir), query.Skip, query.PageSize);
        return PagedResult<PurchaseListItemDto>.Of(rows.Select(ToListItem).ToList(), total, query);
    }

    public async Task<PurchaseSummaryDto> SummaryAsync(PagedQuery query, PurchaseListQuery filter)
    {
        var summary = await _purchases.SummaryAsync(ToFilter(query, filter));
        return new PurchaseSummaryDto
        {
            Count = summary.Count,
            CancelledCount = summary.CancelledCount,
            Totals = summary.Totals.Select(t => new PurchaseCurrencyTotalDto
            {
                CurrencyId = t.CurrencyId,
                CurrencyNotation = t.CurrencyNotation,
                Total = t.Total,
                Paid = t.Paid,
                Due = t.Total - t.Paid,
            }).ToList(),
        };
    }

    public async Task<PurchaseDetailDto?> GetAsync(int id)
        => await _purchases.GetAsync(id) is { } purchase ? await ToDetailAsync(purchase) : null;

    public async Task<PurchaseDetailDto> CreateAsync(PurchaseRequest request, int userId)
    {
        var created = await _purchases.CreateAsync(
            new NewPurchase(
                request.SupplierId,
                request.PurchaseDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Now.Date,
                request.SupplierReference,
                request.Notes,
                request.DiscountPercent,
                request.AmountPaid,
                request.Lines.Select(l => new PurchaseLineInput(l.ProductId, l.Quantity, l.UnitCost, l.UpdateCostPrice)).ToList(),
                request.PayFromDrawer),
            userId);
        return await ToDetailAsync(created);
    }

    public async Task<PurchaseDetailDto?> CancelAsync(int id, int userId, string? reason)
    {
        if (await _purchases.GetAsync(id) is null) return null;
        return await ToDetailAsync(await _purchases.CancelAsync(id, userId, reason));
    }

    public async Task<PurchasePrintResultDto?> PrintAsync(int id)
    {
        if (await _purchases.GetAsync(id) is not { } purchase) return null;

        var document = await BuildDocumentAsync(purchase);
        var printed = await _printer.PrintDocumentAsync(document, jobName: document.Title);
        var pdfUrl = PdfUrl(id);

        _audit.LogBusiness(
            printed ? AuditActions.Print : AuditActions.PrintFailed,
            printed ? $"Sent '{document.Title}' to the receipt printer" : $"Could not print '{document.Title}' - receipt printer unavailable",
            "PrintJob",
            printed ? AuditOutcome.Success : AuditOutcome.Failure,
            printed ? AuditLogSeverity.Info : AuditLogSeverity.Warning,
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, pdfUrl, purchaseId = id });

        return new PurchasePrintResultDto
        {
            Printed = printed,
            Message = _text[printed ? "Pos_PrintOk" : "Pos_PrintFailed"].Value,
            PdfUrl = pdfUrl,
        };
    }

    public async Task<(byte[] Pdf, string FileName)?> PdfAsync(int id)
    {
        if (await _purchases.GetAsync(id) is not { } purchase) return null;

        var document = await BuildDocumentAsync(purchase);
        var pdf = _printer.GeneratePdf(document, PdfPaper.A4);

        _audit.LogBusiness(AuditActions.PdfGenerated, $"PDF generated: '{document.Title}'", "PrintJob",
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, paper = nameof(PdfPaper.A4), bytes = pdf.Length, purchaseId = id });
        return (pdf, $"{document.FileName}.pdf");
    }

    public async Task<byte[]> ExportAsync(PagedQuery query, PurchaseListQuery filter)
    {
        var purchases = await _purchases.ExportAsync(ToFilter(query, filter));
        var inv = CultureInfo.InvariantCulture;
        var csv = new StringBuilder();
        csv.AppendLine("Number,Date,Supplier,Supplier reference,Status,Currency,Product,Barcode,Quantity,Unit cost,Line total,Purchase subtotal,Discount,Purchase total,Paid,Due");
        foreach (var p in purchases)
        {
            var day = p.PurchaseDate.UtcToLocal().ToString("yyyy-MM-dd", inv);
            foreach (var i in p.Items)
            {
                csv.AppendLine(string.Join(',', new[]
                {
                    p.Number, day, p.Supplier.Name, p.SupplierReference, StatusName(p.Status), p.Currency.Notation,
                    i.ProductName, i.Barcode, i.Quantity.ToString("0.####", inv), i.UnitCost.ToString("0.####", inv), i.LineTotal.ToString("0.##", inv),
                    p.Subtotal.ToString("0.##", inv), p.DiscountAmount.ToString("0.##", inv), p.Total.ToString("0.##", inv),
                    p.AmountPaid.ToString("0.##", inv), (p.Total - p.AmountPaid).ToString("0.##", inv),
                }.Select(Csv)));
            }
        }

        // A BOM so Excel opens Arabic supplier and product names correctly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        // A leading = + - @ would run as a formula in Excel.
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static PurchaseFilter ToFilter(PagedQuery query, PurchaseListQuery filter)
    {
        DateTime? from = null, to = null;
        if (filter.From is not null || filter.To is not null)
        {
            var start = filter.From?.ToDateTime(TimeOnly.MinValue) ?? new DateTime(2000, 1, 1);
            var end = filter.To?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Now.Date.AddYears(1);
            (from, to) = AppTime.LocalDayRangeToUtc(start, end);
        }

        PurchaseStatus? status = filter.Status?.ToLowerInvariant() switch
        {
            "received" => PurchaseStatus.Received,
            "cancelled" => PurchaseStatus.Cancelled,
            _ => null,
        };
        return new PurchaseFilter(query.Q, filter.SupplierId, status, from, to);
    }

    private static string StatusName(PurchaseStatus status) => status == PurchaseStatus.Cancelled ? PurchaseStatusNames.Cancelled : PurchaseStatusNames.Received;

    private static string PdfUrl(int id) => $"/api/v1/purchases/{id}/pdf";

    private static PurchaseListItemDto ToListItem(PurchaseListRow r) => new()
    {
        Id = r.Id,
        Number = Purchase.FormatNumber(r.Id),
        PurchaseDate = r.PurchaseDate,
        SupplierId = r.SupplierId,
        SupplierName = r.SupplierName,
        SupplierReference = r.SupplierReference,
        CurrencyNotation = r.CurrencyNotation,
        ItemCount = r.ItemCount,
        Total = r.Total,
        AmountPaid = r.AmountPaid,
        AmountDue = r.Total - r.AmountPaid,
        Status = StatusName(r.Status),
    };

    private async Task<PurchaseDetailDto> ToDetailAsync(Purchase p)
    {
        var people = await _purchases.UserNamesAsync(new[] { p.UserId }.Concat(p.CancelledByUserId is { } c ? new[] { c } : []));
        return new PurchaseDetailDto
        {
            Id = p.Id,
            Number = p.Number,
            PurchaseDate = p.PurchaseDate,
            SupplierId = p.SupplierId,
            SupplierName = p.Supplier.Name,
            SupplierPhone = p.Supplier.Phone,
            SupplierEmail = p.Supplier.Email,
            CurrencyId = p.CurrencyId,
            CurrencyNotation = p.Currency.Notation,
            SupplierReference = p.SupplierReference,
            Notes = p.Notes,
            Subtotal = p.Subtotal,
            DiscountPercent = p.DiscountPercent,
            DiscountAmount = p.DiscountAmount,
            Total = p.Total,
            AmountPaid = p.AmountPaid,
            AmountDue = p.Total - p.AmountPaid,
            PaidFromDrawer = p.CashDropId is not null,
            Status = StatusName(p.Status),
            CreatedAt = p.CreatedAt,
            CreatedBy = people.GetValueOrDefault(p.UserId),
            CancelledAt = p.CancelledAt,
            CancelledBy = p.CancelledByUserId is { } who ? people.GetValueOrDefault(who) : null,
            CancelReason = p.CancelReason,
            Items = p.Items.OrderBy(i => i.Id).Select(i => new PurchaseItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Barcode = i.Barcode,
                Unit = i.Unit,
                Quantity = i.Quantity,
                UnitCost = i.UnitCost,
                LineTotal = i.LineTotal,
                UpdatedCostPrice = i.UpdatedCostPrice,
            }).ToList(),
        };
    }

    private async Task<PrintDocument> BuildDocumentAsync(Purchase p)
    {
        var store = await _store.GetStoreSettingsEntityAsync();
        var inv = CultureInfo.InvariantCulture;
        var notation = p.Currency.Notation;
        var people = await _purchases.UserNamesAsync(new[] { p.UserId });

        var totals = new List<(string Label, string Value)> { ("Subtotal", FormatCurrency(p.Subtotal, notation)) };
        if (p.DiscountAmount > 0)
            totals.Add(($"Discount {p.DiscountPercent.ToString("0.##", inv)}%", "-" + FormatCurrency(p.DiscountAmount, notation)));
        totals.Add(("TOTAL", FormatCurrency(p.Total, notation)));
        if (p.AmountPaid > 0)
            totals.Add(("Paid", FormatCurrency(p.AmountPaid, notation)));
        totals.Add(("Left to pay", FormatCurrency(p.Total - p.AmountPaid, notation)));

        string? cancelled = p.Status == PurchaseStatus.Cancelled
            ? $"Cancelled {p.CancelledAt.UtcToLocal()?.ToString("yyyy-MM-dd HH:mm", inv)}: {p.CancelReason}"
            : null;

        return ReportDocuments.PurchaseReceipt(
            store.StoreName,
            [store.AddressLine1, store.AddressLine2, store.Phone],
            p.Number,
            p.PurchaseDate.UtcToLocal().ToString("yyyy-MM-dd", inv),
            p.Supplier.Name,
            p.SupplierReference,
            people.GetValueOrDefault(p.UserId),
            p.Items.OrderBy(i => i.Id).Select(i => new PurchaseReceiptLine(
                i.ProductName,
                i.Quantity.ToString("0.####", inv) + (string.IsNullOrWhiteSpace(i.Unit) || i.Unit == "piece" ? "" : " " + i.Unit),
                FormatCurrency(i.UnitCost, notation),
                FormatCurrency(i.LineTotal, notation))).ToList(),
            totals,
            p.Notes,
            cancelled);
    }
}
