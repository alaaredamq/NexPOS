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

    public OnlineController(IOnlineApiService online) => _online = online;

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
}
