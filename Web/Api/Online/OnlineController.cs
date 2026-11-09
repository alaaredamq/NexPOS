using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Sync;
using Web.Api.Infrastructure;
using Web.Api.Online.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Online;

/// <summary>
/// Settings &gt; Online: connect this till to NexPOS Online with a link from the provider, see how it is doing, disconnect.
/// Needs <c>Settings.Manage</c>, like the other settings tabs.
/// <para>
/// Deliberately not behind <c>[RequireSalesLicense]</c>, and nothing here is on the selling path: Online is an add-on service,
/// and the till keeps selling whatever state it is in.
/// </para>
/// </summary>
[Route("api/v1/online")]
[RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
public sealed class OnlineController : ApiControllerBase
{
    private readonly IOnlineApiService _online;
    private readonly IOnlineSealingApiService _sealing;

    public OnlineController(IOnlineApiService online, IOnlineSealingApiService sealing)
    {
        _online = online;
        _sealing = sealing;
    }

    /// <summary>Connection state and the result of the last synchronisation.</summary>
    [HttpGet]
    [ProducesResponseType<OnlineStatusDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getOnlineStatus")]
    public OnlineStatusDto Get() => _online.Get();

    /// <summary>
    /// Connects with the provider's link and starts syncing at once (no restart). A refused link answers 400 with a stable code:
    /// <c>enroll_link_invalid</c>, <c>cloud_insecure_url</c>, <c>enroll_code_invalid</c>, <c>enroll_code_expired</c>, <c>enroll_code_used</c>,
    /// <c>site_disabled</c>, <c>rate_limited</c>; <c>cloud_unreachable</c> answers 502. Nothing changes on failure.
    /// </summary>
    [HttpPost("connect")]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType<OnlineStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status502BadGateway, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "connectOnline")]
    public async Task<ActionResult<OnlineStatusDto>> Connect(OnlineConnectRequest request, CancellationToken ct)
    {
        try
        {
            return await _online.ConnectAsync(request.Link, ct);
        }
        catch (OnlineConnectException ex)
        {
            var status = ex.Code is OnlineConnectCodes.Unreachable or OnlineConnectCodes.CloudError
                ? StatusCodes.Status502BadGateway
                : StatusCodes.Status400BadRequest;
            return Failure(status, ex.Code, ex.Message);
        }
    }

    /// <summary>Stops syncing and forgets the credential. Nothing on this till is deleted.</summary>
    [HttpPost("disconnect")]
    [ProducesResponseType<OnlineStatusDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "disconnectOnline")]
    public async Task<OnlineStatusDto> Disconnect(CancellationToken ct) => await _online.DisconnectAsync(ct);

    /// <summary>Asks the background sync to run now rather than at its next turn. Returns the state as it is; poll <c>GET</c> for the outcome.</summary>
    [HttpPost("sync-now")]
    [ProducesResponseType<OnlineStatusDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "syncOnlineNow")]
    public OnlineStatusDto SyncNow() => _online.SyncNow();

    // ---- private Online (docs/online/PRIVACY-KEYS.md) ------------------------------------------------------------------
    // Everything that reveals the key takes the signed-in user's PIN in the request. A wrong PIN is 400 with errors.pin
    // (the same shape as the PIN change); five wrong ones lock the check for five minutes (429 pin_locked).

    /// <summary>The recovery code for the key on this PC (made on first use). 400 <c>errors.pin</c> for a wrong PIN, 429 <c>pin_locked</c>.</summary>
    [HttpPost("sealing/slip")]
    [RequestSizeLimit(4 * 1024)]
    [ProducesResponseType<RecoverySlipDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getRecoverySlip")]
    public Task<ActionResult<RecoverySlipDto>> Slip(SealingPinRequest request) => Guard(() => _sealing.GetSlipAsync(CurrentUserId, request.Pin));

    /// <summary>Prints the recovery slip on the receipt printer. Never fails because of the printer: <c>printed: false</c> comes with a message.</summary>
    [HttpPost("sealing/slip/print")]
    [RequestSizeLimit(4 * 1024)]
    [ProducesResponseType<SlipPrintResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "printRecoverySlip")]
    public Task<ActionResult<SlipPrintResultDto>> PrintSlip(SealingPinRequest request) => Guard(() => _sealing.PrintSlipAsync(CurrentUserId, request.Pin));

    /// <summary>The recovery slip as a PDF (POST, because the PIN travels in the body).</summary>
    [HttpPost("sealing/slip/pdf")]
    [RequestSizeLimit(4 * 1024)]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getRecoverySlipPdf")]
    public async Task<IActionResult> SlipPdf(SealingPinRequest request)
    {
        try
        {
            var (pdf, fileName) = await _sealing.GetSlipPdfAsync(CurrentUserId, request.Pin);
            return File(pdf, "application/pdf", fileName);
        }
        catch (OnlineRuleException ex)
        {
            return Refuse(ex);
        }
    }

    /// <summary>
    /// The owner kept the slip: sealing starts. The next upload is the whole shop again, sealed, which replaces the cloud's
    /// readable copy. 409 <c>not_connected</c> or <c>no_key</c>.
    /// </summary>
    [HttpPost("sealing/confirm")]
    [ProducesResponseType<OnlineStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "confirmSealing")]
    public Task<ActionResult<OnlineStatusDto>> ConfirmSealing() => Guard(() => Task.FromResult(_sealing.Confirm(CurrentUserId)));

    /// <summary>Puts the key from a recovery slip on this PC (a restore on a new machine). 400 <c>recovery_code_invalid</c>, 409 <c>key_exists</c>.</summary>
    [HttpPost("sealing/restore")]
    [RequestSizeLimit(4 * 1024)]
    [ProducesResponseType<OnlineStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "restoreSealKey")]
    public Task<ActionResult<OnlineStatusDto>> RestoreKey(SealingRestoreRequest request) => Guard(() => _sealing.RestoreAsync(CurrentUserId, request.Pin, request.Code));

    private async Task<ActionResult<T>> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OnlineRuleException ex)
        {
            return Refuse(ex);
        }
    }

    private ObjectResult Refuse(OnlineRuleException ex)
        => ex.Field is not null ? ValidationFailure(ex.Field, ex.Message) : Failure(ex.Status, ex.Code, ex.Message);
}
