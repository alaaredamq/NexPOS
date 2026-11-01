using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Orders.Contracts;
using Web.Api.Pos;
using Web.Api.Receipts.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Receipts;

/// <summary>Receipts: list, stats, details, void (reverses the sale), reprint and PDF.</summary>
/// <remarks>
/// Port of the old <c>ReceiptsController</c>: the whole controller needed Sales.View, voiding Sales.Edit.
/// Not ported: emailing a receipt (the old action never sent anything, it only recorded "emailed").
/// </remarks>
[Route("api/v1/receipts")]
[SwaggerTag("Receipts")]
public sealed class ReceiptsController : PosControllerBase
{
    private readonly IReceiptQueryService _receipts;

    public ReceiptsController(IReceiptQueryService receipts) => _receipts = receipts;

    /// <summary>One page of the receipts list, newest first.</summary>
    /// <remarks>
    /// <c>q</c> matches the receipt number, the customer's name or phone, or the total. <c>status</c> is <c>valid</c> or <c>void</c> (omitted = both).
    /// <c>sort</c> is one of <c>date</c> (default, descending), <c>number</c>, <c>customer</c>, <c>cashier</c>, <c>amount</c>, <c>payment</c>, <c>voidDate</c>.
    /// <c>from</c> / <c>to</c> are local days (yyyy-MM-dd).
    /// </remarks>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [SwaggerOperation(OperationId = "listReceipts")]
    [ProducesResponseType<PagedResult<ReceiptListItemDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ReceiptListItemDto>> List([FromQuery] PagedQuery query, [FromQuery] ReceiptListQuery filter)
        => await _receipts.ListAsync(query, filter);

    /// <summary>Sales tiles and analytics for a range of local days (default: the last 30 days up to today).</summary>
    /// <remarks>Money is totalled per currency, never across currencies. Void receipts only count in <c>voidCount</c>.</remarks>
    [HttpGet("stats")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [SwaggerOperation(OperationId = "getReceiptStats")]
    [ProducesResponseType<ReceiptStatsDto>(StatusCodes.Status200OK)]
    public async Task<ReceiptStatsDto> Stats([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        => await _receipts.StatsAsync(from, to);

    /// <summary>One receipt with its lines, payments and totals.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [SwaggerOperation(OperationId = "getReceipt")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    public async Task<ReceiptDetailDto> Get(int id) => await _receipts.GetAsync(id);

    /// <summary>Voids the receipt and reverses the sale behind it.</summary>
    /// <remarks>
    /// Same all-or-nothing reversal as cancelling the order: receipt voided, order cancelled, stock back, debt off the customer's account, drawer corrected.
    /// 409 <c>receipt_already_void</c>. A reason is required.
    /// </remarks>
    [HttpPost("{id:int}/void")]
    [RequirePermission(PermissionHelper.Permissions.Sales_Edit)]
    [SwaggerOperation(OperationId = "voidReceipt")]
    [ProducesResponseType<SaleReversalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    public async Task<SaleReversalDto> Void(int id, [FromBody] ReceiptVoidRequest request)
        => await _receipts.VoidAsync(id, request.Reason.Trim(), CurrentUserId);

    /// <summary>Reprints the receipt on the receipt printer. Never fails because of the printer: <c>printed: false</c> comes with a message and the PDF link.</summary>
    [HttpPost("{id:int}/print")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [SwaggerOperation(OperationId = "printReceipt")]
    [ProducesResponseType<ReceiptPrintResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    public async Task<ReceiptPrintResultDto> Print(int id) => await _receipts.PrintAsync(id);

    /// <summary>The receipt as a PDF (receipt-roll paper).</summary>
    [HttpGet("{id:int}/pdf")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [SwaggerOperation(OperationId = "getReceiptPdf")]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    public async Task<IActionResult> Pdf(int id)
    {
        var (pdf, fileName) = await _receipts.GetPdfAsync(id);
        return File(pdf, "application/pdf", fileName);
    }
}
