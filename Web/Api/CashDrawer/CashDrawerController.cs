using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.CashDrawer.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.CashDrawer;

/// <summary>
/// The cash drawer: open, running figures, cash drops, close, history and the Z report. Reading and opening need <c>Sales.View</c>
/// <b>or</b> <c>Sales.Manage</c> (the selling permission), so a cashier opens their own drawer and their cash sales land in it; without
/// <c>Sales.View</c> only their own sessions are visible (another one answers 404, like a wrong id), and Sales.View still guards the
/// receipts list. Closing a session and dropping cash need <c>Sales.Edit</c>: a manager counts the shift.
/// </summary>
[Route("api/v1/cash-drawer")]
[DrawerRuleExceptionFilter]
public sealed class CashDrawerController : ApiControllerBase
{
    private readonly ICashDrawerApiService _drawer;

    public CashDrawerController(ICashDrawerApiService drawer) => _drawer = drawer;

    /// <summary>Managers (Sales.View) see every session; a cashier only their own.</summary>
    private bool SeesAllSessions => PermissionHelper.HasPermission(User, PermissionHelper.Permissions.Sales_View);

    private void EnsureVisible(DrawerSessionDto session)
    {
        if (!SeesAllSessions && session.UserId != CurrentUserId)
            throw new DrawerRuleException(StatusCodes.Status404NotFound, DrawerCodes.SessionNotFound, "Drawer_SessionNotFound");
    }

    private async Task EnsureVisibleAsync(int sessionId)
    {
        if (!SeesAllSessions) EnsureVisible(await _drawer.GetSessionAsync(sessionId));
    }

    /// <summary>The signed-in user's drawer: open or not, and the open session with its running figures.</summary>
    [HttpGet("current")]
    [RequireAnyPermission(PermissionHelper.Permissions.Sales_View, PermissionHelper.Permissions.Sales_Manage)]
    [ProducesResponseType<CurrentDrawerDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getCurrentDrawer")]
    public async Task<CurrentDrawerDto> Current() => await _drawer.GetCurrentAsync(CurrentUserId);

    /// <summary>Opens the signed-in user's drawer with a starting float. 409 <c>drawer_already_open</c> when one is open.</summary>
    [HttpPost("open")]
    [RequireAnyPermission(PermissionHelper.Permissions.Sales_View, PermissionHelper.Permissions.Sales_Manage)]
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
    [RequireAnyPermission(PermissionHelper.Permissions.Sales_View, PermissionHelper.Permissions.Sales_Manage)]
    [ProducesResponseType<PagedResult<DrawerHistoryRowDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listDrawerSessions")]
    public async Task<PagedResult<DrawerHistoryRowDto>> History([FromQuery] PagedQuery query, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        => await _drawer.GetHistoryAsync(CurrentUserId, from, to, query);

    /// <summary>One session with its figures and cash drops (any user's for Sales.View, as the Razor close and Z report pages allowed; otherwise only your own).</summary>
    [HttpGet("sessions/{id:int}")]
    [RequireAnyPermission(PermissionHelper.Permissions.Sales_View, PermissionHelper.Permissions.Sales_Manage)]
    [ProducesResponseType<DrawerSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getDrawerSession")]
    public async Task<DrawerSessionDto> GetSession(int id)
    {
        var session = await _drawer.GetSessionAsync(id);
        EnsureVisible(session);
        return session;
    }

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
    [RequireAnyPermission(PermissionHelper.Permissions.Sales_View, PermissionHelper.Permissions.Sales_Manage)]
    [ProducesResponseType<ZReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getZReport")]
    public async Task<ZReportDto> ZReport(int id)
    {
        await EnsureVisibleAsync(id);
        return await _drawer.GetZReportAsync(id);
    }

    /// <summary>Sends the Z report to the receipt printer. Never fails because of the printer: <c>printed: false</c> comes with a message and the PDF link.</summary>
    [HttpPost("sessions/{id:int}/z-report/print")]
    [RequireAnyPermission(PermissionHelper.Permissions.Sales_View, PermissionHelper.Permissions.Sales_Manage)]
    [ProducesResponseType<ZReportPrintResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "printZReport")]
    public async Task<ZReportPrintResultDto> PrintZReport(int id)
    {
        await EnsureVisibleAsync(id);
        return await _drawer.PrintZReportAsync(id);
    }

    /// <summary>The Z report as an A4 PDF.</summary>
    [HttpGet("sessions/{id:int}/z-report.pdf")]
    [RequireAnyPermission(PermissionHelper.Permissions.Sales_View, PermissionHelper.Permissions.Sales_Manage)]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getZReportPdf")]
    public async Task<IActionResult> ZReportPdf(int id)
    {
        await EnsureVisibleAsync(id);
        var (pdf, fileName) = await _drawer.GetZReportPdfAsync(id);
        return File(pdf, "application/pdf", fileName);
    }
}
