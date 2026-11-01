using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.CashDrawer.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.CashDrawer;

/// <summary>
/// The cash drawer: open, running figures, cash drops, close, history and the Z report. Mirrors the Razor <c>CashDrawerController</c>:
/// everything needs <c>Sales.View</c>; closing a session and dropping cash also need <c>Sales.Edit</c>.
/// </summary>
[Route("api/v1/cash-drawer")]
[DrawerRuleExceptionFilter]
public sealed class CashDrawerController : ApiControllerBase
{
    private readonly ICashDrawerApiService _drawer;

    public CashDrawerController(ICashDrawerApiService drawer) => _drawer = drawer;

    /// <summary>The signed-in user's drawer: open or not, and the open session with its running figures.</summary>
    [HttpGet("current")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [ProducesResponseType<CurrentDrawerDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getCurrentDrawer")]
    public async Task<CurrentDrawerDto> Current() => await _drawer.GetCurrentAsync(CurrentUserId);

    /// <summary>Opens the signed-in user's drawer with a starting float. 409 <c>drawer_already_open</c> when one is open.</summary>
    [HttpPost("open")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [ProducesResponseType<DrawerSessionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "openDrawer")]
    public async Task<ActionResult<DrawerSessionDto>> Open(OpenDrawerRequest request)
    {
        var session = await _drawer.OpenAsync(CurrentUserId, request);
        return CreatedAtAction(nameof(GetSession), new { id = session.Id }, session);
    }

    /// <summary>The user's own sessions, newest first. <c>from</c> / <c>to</c> are local dates (<c>2026-11-01</c>) and include both days.</summary>
    [HttpGet("sessions")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [ProducesResponseType<PagedResult<DrawerHistoryRowDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listDrawerSessions")]
    public async Task<PagedResult<DrawerHistoryRowDto>> History([FromQuery] PagedQuery query, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        => await _drawer.GetHistoryAsync(CurrentUserId, from, to, query);

    /// <summary>One session with its figures and cash drops (any user's, as the Razor close and Z report pages allowed).</summary>
    [HttpGet("sessions/{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [ProducesResponseType<DrawerSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getDrawerSession")]
    public async Task<DrawerSessionDto> GetSession(int id) => await _drawer.GetSessionAsync(id);

    /// <summary>Closes a session against the counted cash. The difference to the expected cash is stored with it.</summary>
    [HttpPost("sessions/{id:int}/close")]
    [RequirePermission(PermissionHelper.Permissions.Sales_Edit)]
    [ProducesResponseType<DrawerSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "closeDrawerSession")]
    public async Task<DrawerSessionDto> Close(int id, CloseDrawerRequest request) => await _drawer.CloseAsync(id, request);

    /// <summary>Takes cash out of an open session.</summary>
    [HttpPost("sessions/{id:int}/drops")]
    [RequirePermission(PermissionHelper.Permissions.Sales_Edit)]
    [ProducesResponseType<CashDropDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "addCashDrop")]
    public async Task<ActionResult<CashDropDto>> AddDrop(int id, CashDropRequest request)
    {
        var drop = await _drawer.AddCashDropAsync(id, CurrentUserId, request);
        return Created($"/api/v1/cash-drawer/sessions/{id}", drop);
    }

    /// <summary>The Z report of a closed session. 409 <c>session_still_open</c> while it is open.</summary>
    [HttpGet("sessions/{id:int}/z-report")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [ProducesResponseType<ZReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getZReport")]
    public async Task<ZReportDto> ZReport(int id) => await _drawer.GetZReportAsync(id);

    /// <summary>Sends the Z report to the receipt printer. Never fails because of the printer: <c>printed: false</c> comes with a message and the PDF link.</summary>
    [HttpPost("sessions/{id:int}/z-report/print")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [ProducesResponseType<ZReportPrintResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "printZReport")]
    public async Task<ZReportPrintResultDto> PrintZReport(int id) => await _drawer.PrintZReportAsync(id);

    /// <summary>The Z report as an A4 PDF.</summary>
    [HttpGet("sessions/{id:int}/z-report.pdf")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getZReportPdf")]
    public async Task<IActionResult> ZReportPdf(int id)
    {
        var (pdf, fileName) = await _drawer.GetZReportPdfAsync(id);
        return File(pdf, "application/pdf", fileName);
    }
}
