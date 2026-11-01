using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Pos.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Pos;

/// <summary>The sale itself and what follows it: checkout, the cashier's last sales, reprint, PDF.</summary>
[Route("api/v1/pos/sales")]
public sealed class PosSalesController : PosControllerBase
{
    private readonly IPosSaleService _sales;

    public PosSalesController(IPosSaleService sales) => _sales = sales;

    /// <summary>Completes a sale: stock, order, receipt, drawer, debt - all or nothing - then prints the receipt if asked.</summary>
    /// <remarks>
    /// Send the cart (never prices), the customer, how it is paid, the total the customer pays and a fresh <c>idempotencyKey</c>. The server prices the cart itself;
    /// <c>totalAmount</c> may differ from its total only by a cashier's discount or cash rounding (more than the greater of 1.00 / 2 % is <c>total_mismatch</c>).
    /// <para>
    /// <b>Network failure:</b> retry with the <b>same</b> key. If the first attempt did go through you get that sale back (<c>replayed: true</c>); nothing is sold twice. Nothing is lost client-side until you get a success.
    /// </para>
    /// <para>
    /// Refusals (409 unless noted): <c>insufficient_stock</c> (offer "sell anyway": resend with <c>allowInsufficientStock</c>), <c>drawer_not_open</c> (only with <c>requireOpenDrawer</c>),
    /// <c>total_mismatch</c>, <c>customer_not_found</c>, <c>idempotency_key_reused</c> (same key, different cart), <c>checkout_failed</c> (any other rule of the checkout service; <c>detail</c> says which),
    /// 403 <c>license_sales_stopped</c>; 400 <c>payment_method_invalid</c>, <c>debt_requires_customer</c> and the pricing codes of <c>pos/price</c>.
    /// A refused sale leaves nothing behind.
    /// </para>
    /// </remarks>
    [HttpPost("checkout")]
    [AnySignedInUser]
    [RequireSalesLicense]
    [ProducesResponseType<PosSaleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "posCheckout")]
    public async Task<ActionResult<PosSaleDto>> Checkout(PosCheckoutRequest request) => await _sales.CheckoutAsync(request, CurrentUserId);

    /// <summary>The signed-in cashier's latest sales (last 7 days, voided ones left out), newest first - "reprint the last receipt".</summary>
    [HttpGet("recent")]
    [RequirePermission(PermissionHelper.Permissions.Orders_View)]
    [ProducesResponseType<IReadOnlyList<PosSaleSummaryDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getRecentPosSales")]
    public async Task<ActionResult<IReadOnlyList<PosSaleSummaryDto>>> Recent([FromQuery] int count = 5)
        => Ok(await _sales.GetRecentAsync(CurrentUserId, count));

    /// <summary>Reprints a sale's receipt on the receipt printer. Never fails because of the printer: <c>printed: false</c> comes with a message and the PDF link.</summary>
    [HttpPost("{orderId:int}/print")]
    [RequirePermission(PermissionHelper.Permissions.Orders_View)]
    [ProducesResponseType<PosPrintResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "printPosSale")]
    public async Task<ActionResult<PosPrintResultDto>> Print(int orderId) => await _sales.PrintAsync(orderId);

    /// <summary>The receipt as a PDF (receipt-roll paper).</summary>
    [HttpGet("{orderId:int}/receipt.pdf")]
    [RequirePermission(PermissionHelper.Permissions.Orders_View)]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getPosSaleReceiptPdf")]
    public async Task<IActionResult> ReceiptPdf(int orderId)
    {
        var (pdf, fileName) = await _sales.GetPdfAsync(orderId);
        return File(pdf, "application/pdf", fileName);
    }
}
