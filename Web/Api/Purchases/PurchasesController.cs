using Application.IServices.Purchasing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Purchases.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Purchases;

/// <summary>
/// Purchase notes: goods bought from a supplier. Saving one adds every line to the product's stock and puts what was not
/// paid on the supplier's balance; cancelling takes both back. The receipt prints on the receipt printer or downloads as a PDF.
/// </summary>
[Route("api/v1/purchases")]
[SwaggerTag("Purchases")]
public sealed class PurchasesController : ApiControllerBase
{
    private readonly IPurchaseApiService _purchases;
    private readonly IStringLocalizer<SharedResource> _text;

    public PurchasesController(IPurchaseApiService purchases, IStringLocalizer<SharedResource> text)
    {
        _purchases = purchases;
        _text = text;
    }

    /// <summary>One page of purchases, newest first. <c>sort</c>: <c>date</c> (default), <c>number</c>, <c>supplier</c>, <c>reference</c>, <c>total</c>, <c>status</c>. <c>q</c> matches the number, the supplier and the supplier's reference; <c>from</c> / <c>to</c> are local days.</summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Purchases_View)]
    [ProducesResponseType<PagedResult<PurchaseListItemDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listPurchases")]
    public async Task<PagedResult<PurchaseListItemDto>> List([FromQuery] PagedQuery query, [FromQuery] PurchaseListQuery filter)
        => await _purchases.ListAsync(query, filter);

    /// <summary>The tiles: how many purchases and how much was bought, per currency, for the same supplier / date / search filter (the status filter is ignored).</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionHelper.Permissions.Purchases_View)]
    [ProducesResponseType<PurchaseSummaryDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getPurchasesSummary")]
    public async Task<PurchaseSummaryDto> Summary([FromQuery] PagedQuery query, [FromQuery] PurchaseListQuery filter)
        => await _purchases.SummaryAsync(query, filter);

    /// <summary>The filtered purchases, one CSV row per product line.</summary>
    [HttpGet("export")]
    [RequirePermission(PermissionHelper.Permissions.Purchases_View)]
    [Produces("text/csv")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    [SwaggerOperation(OperationId = "exportPurchases")]
    public async Task<IActionResult> Export([FromQuery] PagedQuery query, [FromQuery] PurchaseListQuery filter)
        => File(await _purchases.ExportAsync(query, filter), "text/csv", $"purchases_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

    /// <summary>One purchase with its lines.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Purchases_View)]
    [ProducesResponseType<PurchaseDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getPurchase")]
    public async Task<ActionResult<PurchaseDetailDto>> Get(int id) => await _purchases.GetAsync(id) is { } purchase ? purchase : NotFound();

    /// <summary>Saves a purchase: adds the quantities to stock, optionally updates cost prices, moves the supplier's balance and, with <c>payFromDrawer</c>, takes the paid amount out of the cash drawer. All or nothing. 400 with a <c>code</c> when a rule is broken; 409 <c>drawer_not_open</c> when paying from a drawer that is not open.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Purchases_Create)]
    [ProducesResponseType<PurchaseDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createPurchase")]
    public async Task<ActionResult<PurchaseDetailDto>> Create(PurchaseRequest request)
    {
        if (CurrentUserId == 0) return Unauthorized();
        try
        {
            var created = await _purchases.CreateAsync(request, CurrentUserId);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (PurchaseRuleException ex)
        {
            return Rule(ex);
        }
    }

    /// <summary>Cancels a purchase: takes its stock back out and removes it from the supplier's balance. 409 <c>stock_already_sold</c> when part of the goods was sold since; 409 <c>already_cancelled</c>.</summary>
    [HttpPost("{id:int}/cancel")]
    [RequirePermission(PermissionHelper.Permissions.Purchases_Cancel)]
    [ProducesResponseType<PurchaseDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "cancelPurchase")]
    public async Task<ActionResult<PurchaseDetailDto>> Cancel(int id, PurchaseCancelRequest request)
    {
        if (CurrentUserId == 0) return Unauthorized();
        try
        {
            return await _purchases.CancelAsync(id, CurrentUserId, request.Reason) is { } cancelled ? cancelled : NotFound();
        }
        catch (PurchaseRuleException ex)
        {
            return Rule(ex);
        }
    }

    /// <summary>Sends the purchase receipt to the receipt printer. Answers 200 with <c>printed: false</c> when the printer is unavailable.</summary>
    [HttpPost("{id:int}/print")]
    [RequirePermission(PermissionHelper.Permissions.Purchases_View)]
    [ProducesResponseType<PurchasePrintResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "printPurchase")]
    public async Task<ActionResult<PurchasePrintResultDto>> Print(int id) => await _purchases.PrintAsync(id) is { } result ? result : NotFound();

    /// <summary>The purchase receipt as an A4 PDF.</summary>
    [HttpGet("{id:int}/pdf")]
    [RequirePermission(PermissionHelper.Permissions.Purchases_View)]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "downloadPurchasePdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        if (await _purchases.PdfAsync(id) is not { } file) return NotFound();
        return File(file.Pdf, "application/pdf", file.FileName);
    }

    private ObjectResult Rule(PurchaseRuleException ex)
    {
        var message = _text["Pur_" + ex.Code, ex.Subject ?? string.Empty].Value;
        return ex.Code is PurchaseRuleException.AlreadyCancelled or PurchaseRuleException.StockSold or PurchaseRuleException.DrawerNotOpen
            ? ConflictFailure(ex.Code, message)
            : Failure(StatusCodes.Status400BadRequest, ex.Code, message);
    }
}
