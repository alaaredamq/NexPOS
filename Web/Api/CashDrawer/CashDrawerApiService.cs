using Application.IServices.Sales;
using Core.Auditing;
using Core.Entities.Logging;
using Core.Entities.Sales;
using Core.Printing;
using Core.Time;
using Microsoft.Extensions.Localization;
using Web.Api.CashDrawer.Contracts;
using Web.Api.Infrastructure;
using Web.Api.Session.Contracts;

namespace Web.Api.CashDrawer;

public interface ICashDrawerApiService
{
    Task<CurrentDrawerDto> GetCurrentAsync(int userId);

    /// <summary>The user's own sessions whose opening falls in the local day range, newest first.</summary>
    Task<PagedResult<DrawerHistoryRowDto>> GetHistoryAsync(int userId, DateOnly? from, DateOnly? to, PagedQuery query);

    /// <exception cref="DrawerRuleException">404 for an unknown session.</exception>
    Task<DrawerSessionDto> GetSessionAsync(int sessionId);

    /// <exception cref="DrawerRuleException">409 <c>drawer_already_open</c> when the user already has an open session.</exception>
    Task<DrawerSessionDto> OpenAsync(int userId, OpenDrawerRequest request);

    /// <exception cref="DrawerRuleException">404 for an unknown session, 409 <c>session_closed</c> when it is closed already.</exception>
    Task<DrawerSessionDto> CloseAsync(int sessionId, CloseDrawerRequest request);

    /// <exception cref="DrawerRuleException">404 for an unknown session, 409 <c>session_closed</c> when it is closed.</exception>
    Task<CashDropDto> AddCashDropAsync(int sessionId, int userId, CashDropRequest request);

    /// <exception cref="DrawerRuleException">404 for an unknown session, 409 <c>session_still_open</c> while it is open.</exception>
    Task<ZReportDto> GetZReportAsync(int sessionId);

    Task<ZReportPrintResultDto> PrintZReportAsync(int sessionId);

    Task<(byte[] Pdf, string FileName)> GetZReportPdfAsync(int sessionId);
}

/// <summary>Maps the shared drawer service to the API's DTOs. The sums (expected cash, discounts, adjustments) stay in <see cref="ICashDrawerService"/>.</summary>
public sealed class CashDrawerApiService : ICashDrawerApiService
{
    private readonly ICashDrawerService _drawer;
    private readonly IThermalPrinterService _printer;
    private readonly IAuditTrail _audit;
    private readonly IStringLocalizer<SharedResource> _text;

    public CashDrawerApiService(ICashDrawerService drawer, IThermalPrinterService printer, IAuditTrail audit, IStringLocalizer<SharedResource> text)
    {
        _drawer = drawer;
        _printer = printer;
        _audit = audit;
        _text = text;
    }

    public async Task<CurrentDrawerDto> GetCurrentAsync(int userId)
    {
        var open = await _drawer.GetOpenSessionByUserIdAsync(userId);
        if (open is null)
            return new CurrentDrawerDto { Status = new DrawerStatusDto { IsOpen = false, OpeningCash = 0m } };

        return new CurrentDrawerDto
        {
            Status = new DrawerStatusDto { IsOpen = true, SessionId = open.Id, OpeningCash = open.OpeningCash, OpenedAt = open.OpenedAt },
            Session = ToDto(open, await _drawer.GetSessionSummaryAsync(open.Id)),
        };
    }

    public async Task<PagedResult<DrawerHistoryRowDto>> GetHistoryAsync(int userId, DateOnly? from, DateOnly? to, PagedQuery query)
    {
        // The pickers choose local calendar days; sessions are stored in UTC, so the range runs from the first local day's start to the last local day's end.
        DateTime? fromUtc = from is { } f ? f.ToDateTime(TimeOnly.MinValue).LocalToUtc() : null;
        DateTime? toUtc = to is { } t ? AppTime.LocalDayStartUtc(t.ToDateTime(TimeOnly.MinValue).AddDays(1)).AddTicks(-1) : null;

        var sessions = await _drawer.GetSessionHistoryAsync(userId, fromUtc, toUtc);
        var rows = sessions.Select(s => new DrawerHistoryRowDto
        {
            Id = s.Id,
            IsOpen = s.IsOpen,
            OpenedAt = s.OpenedAt,
            ClosedAt = s.ClosedAt,
            OpeningCash = s.OpeningCash,
            ClosingCash = s.ActualCash,
            TotalSales = s.TotalSales,
            TotalTransactions = s.TotalTransactions,
            CashDifference = s.CashDifference,
        }).ToList();
        return PagedResult<DrawerHistoryRowDto>.From(rows, query);
    }

    public async Task<DrawerSessionDto> GetSessionAsync(int sessionId)
    {
        var session = await FindAsync(sessionId);
        // An open session shows its running figures; a closed one what was stored when it was closed (an order voided later must not rewrite a closed day).
        return ToDto(session, session.IsOpen ? await _drawer.GetSessionSummaryAsync(sessionId) : null);
    }

    public async Task<DrawerSessionDto> OpenAsync(int userId, OpenDrawerRequest request)
    {
        if (await _drawer.HasOpenSessionAsync(userId))
            throw new DrawerRuleException(StatusCodes.Status409Conflict, DrawerCodes.AlreadyOpen, "Drawer_AlreadyOpen");

        var session = await _drawer.OpenDrawerAsync(userId, request.OpeningCash, Clean(request.Notes));
        return await GetSessionAsync(session.Id);
    }

    public async Task<DrawerSessionDto> CloseAsync(int sessionId, CloseDrawerRequest request)
    {
        var session = await FindAsync(sessionId);
        if (!session.IsOpen) throw Closed();

        await _drawer.CloseDrawerAsync(sessionId, request.ActualCash, Clean(request.Notes));
        return await GetSessionAsync(sessionId);
    }

    public async Task<CashDropDto> AddCashDropAsync(int sessionId, int userId, CashDropRequest request)
    {
        // Look first: the shared service signals "no such session" and "closed" with untyped exceptions.
        var session = await FindAsync(sessionId);
        if (!session.IsOpen) throw Closed();

        var drop = await _drawer.AddCashDropAsync(sessionId, userId, request.Amount, Clean(request.Reason), Clean(request.Notes));
        return ToDto(drop);
    }

    public async Task<ZReportDto> GetZReportAsync(int sessionId)
    {
        var report = await GenerateAsync(sessionId);
        return new ZReportDto
        {
            SessionId = report.SessionId,
            CashierName = report.CashierName,
            OpenedAt = report.OpenedAt,
            ClosedAt = report.ClosedAt,
            OpeningCash = report.OpeningCash,
            TotalTransactions = report.TotalTransactions,
            TotalSales = report.TotalSales,
            TotalDiscounts = report.TotalDiscounts,
            TotalAdjustments = report.TotalAdjustments,
            NetSales = report.NetSales,
            CashSales = report.CashSales,
            CardSales = report.CardSales,
            OtherPayments = report.OtherPayments,
            TotalCashDrops = report.TotalCashDrops,
            CashDropCount = report.CashDropCount,
            ExpectedCash = report.ExpectedCash,
            ActualCash = report.ActualCash,
            CashDifference = report.CashDifference,
            CashDrops = report.CashDrops
                .Select(d => new CashDropDto { Id = d.Id, Amount = d.Amount, DroppedAt = d.DroppedAt, Reason = d.Reason, Notes = d.Notes })
                .ToList(),
            Orders = report.Orders.Select(o => new ZReportOrderDto
            {
                OrderId = o.OrderId,
                OrderNumber = o.OrderNumber,
                OrderDate = o.OrderDate,
                // The shared report says "Walk-in" (English) for a sale without a customer; the page words that itself.
                CustomerName = o.CustomerName == "Walk-in" ? string.Empty : o.CustomerName,
                TotalAmount = o.TotalAmount,
                PaymentMethod = o.PaymentMethod,
            }).ToList(),
        };
    }

    public async Task<ZReportPrintResultDto> PrintZReportAsync(int sessionId)
    {
        var document = Application.Services.Printing.ReportDocuments.ZReport(await GenerateAsync(sessionId));
        var printed = await _printer.PrintDocumentAsync(document, jobName: document.Title);

        _audit.LogBusiness(
            printed ? AuditActions.Print : AuditActions.PrintFailed,
            printed ? $"Sent '{document.Title}' to the receipt printer" : $"Could not print '{document.Title}' - receipt printer unavailable",
            "PrintJob",
            printed ? AuditOutcome.Success : AuditOutcome.Failure,
            printed ? AuditLogSeverity.Info : AuditLogSeverity.Warning,
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, sessionId });

        return new ZReportPrintResultDto
        {
            Printed = printed,
            Message = _text[printed ? "Pos_PrintOk" : "Pos_PrintFailed"].Value,
            PdfUrl = $"/api/v1/cash-drawer/sessions/{sessionId}/z-report.pdf",
        };
    }

    public async Task<(byte[] Pdf, string FileName)> GetZReportPdfAsync(int sessionId)
    {
        var document = Application.Services.Printing.ReportDocuments.ZReport(await GenerateAsync(sessionId));
        var pdf = _printer.GeneratePdf(document, PdfPaper.A4);

        _audit.LogBusiness(AuditActions.PdfGenerated, $"PDF generated: '{document.Title}'", "PrintJob",
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, paper = nameof(PdfPaper.A4), bytes = pdf.Length });
        return (pdf, $"{document.FileName}.pdf");
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private async Task<CashDrawerSession> FindAsync(int sessionId)
        => await _drawer.GetSessionWithDetailsAsync(sessionId)
           ?? throw new DrawerRuleException(StatusCodes.Status404NotFound, DrawerCodes.SessionNotFound, "Drawer_SessionNotFound");

    private static DrawerRuleException Closed()
        => new(StatusCodes.Status409Conflict, DrawerCodes.SessionClosed, "Drawer_SessionClosed");

    /// <summary>The Z report of a closed session. One closed by a very old version may lack the stored totals the report reads: that is a 409, not a 500.</summary>
    private async Task<ZReport> GenerateAsync(int sessionId)
    {
        var session = await FindAsync(sessionId);
        if (session.IsOpen)
            throw new DrawerRuleException(StatusCodes.Status409Conflict, DrawerCodes.SessionStillOpen, "Drawer_SessionStillOpen");

        try
        {
            return await _drawer.GenerateZReportAsync(sessionId);
        }
        catch (InvalidOperationException)
        {
            throw new DrawerRuleException(StatusCodes.Status409Conflict, DrawerCodes.ZReportUnavailable, "Drawer_ZReportUnavailable");
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static CashDropDto ToDto(CashDrop drop)
        => new() { Id = drop.Id, Amount = drop.Amount, DroppedAt = drop.DroppedAt, Reason = drop.Reason, Notes = drop.Notes };

    private static DrawerSessionDto ToDto(CashDrawerSession s, SessionSummary? running)
    {
        var drops = s.CashDrops.OrderBy(d => d.DroppedAt).ThenBy(d => d.Id).Select(ToDto).ToList();
        return new DrawerSessionDto
        {
            Id = s.Id,
            UserId = s.UserId,
            CashierName = s.User is null ? string.Empty : $"{s.User.FirstName} {s.User.LastName}".Trim(),
            IsOpen = s.IsOpen,
            OpenedAt = s.OpenedAt,
            ClosedAt = s.ClosedAt,
            OpeningCash = s.OpeningCash,
            ExpectedCash = running?.ExpectedCash ?? s.ExpectedCash ?? s.OpeningCash,
            ActualCash = s.ActualCash,
            CashDifference = s.CashDifference,
            TotalSales = running?.TotalSales ?? s.TotalSales ?? 0m,
            TotalTransactions = running?.TotalTransactions ?? s.TotalTransactions ?? 0,
            TotalDiscounts = running?.TotalDiscounts ?? s.TotalDiscounts ?? 0m,
            TotalAdjustments = running?.TotalAdjustments ?? s.TotalAdjustments ?? 0m,
            TotalCashDrops = running?.TotalCashDrops ?? s.TotalCashDrops ?? drops.Sum(d => d.Amount),
            OpeningNotes = s.OpeningNotes,
            ClosingNotes = s.ClosingNotes,
            CashDrops = drops,
        };
    }
}
