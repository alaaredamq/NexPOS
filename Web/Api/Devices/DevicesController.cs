using Application.DTOs.Licensing;
using Application.IServices.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Devices.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Devices;

/// <summary>
/// The PCs that may sell through this store server, behind a multi-seat licence. An administrator (<c>License.Manage</c>) adds a till
/// and gets a one-time pairing code; the till opens the app, enters the code on its pairing screen and from then on carries a device
/// cookie. The server PC itself is always the first seat and is never listed. Seats are decided here, offline, from the signed key.
/// <para>
/// Not behind <c>[RequireSalesLicense]</c>: pairing a till is what makes it able to sell, and the administrator must be able to manage
/// seats while selling is stopped.
/// </para>
/// </summary>
[Route("api/v1/devices")]
public sealed class DevicesController : ApiControllerBase
{
    private readonly ITillDeviceService _tills;
    private readonly IStringLocalizer<SharedResource> _text;

    public DevicesController(ITillDeviceService tills, IStringLocalizer<SharedResource> text)
    {
        _tills = tills;
        _text = text;
    }

    /// <summary>The package and its tills.</summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.License_Manage)]
    [ProducesResponseType<TillSeatsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getTillSeats")]
    public async Task<TillSeatsDto> Get(CancellationToken ct) => ToDto(await _tills.GetSeatsAsync(ct));

    /// <summary>
    /// Adds a till: reserves a seat and returns the pairing code (shown once). 409 <c>seats_full</c> when every PC of the package is in
    /// use; 403 <c>license_sales_stopped</c> while the licence is not active.
    /// </summary>
    [HttpPost("pairing-codes")]
    [RequirePermission(PermissionHelper.Permissions.License_Manage)]
    [ProducesResponseType<PairingCodeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createTillPairingCode")]
    public async Task<ActionResult<PairingCodeDto>> CreatePairingCode(PairingCodeRequest request, CancellationToken ct)
    {
        var result = await _tills.IssuePairingCodeAsync(request.Name, ct);
        return result.Outcome switch
        {
            IssuePairingOutcome.Success => new PairingCodeDto { DeviceId = result.DeviceId!.Value, Code = result.Code!, ExpiresAt = result.ExpiresAt!.Value },
            IssuePairingOutcome.SeatsFull => ConflictFailure(ApiCodes.SeatsFull, _text["License_SeatsFull"].Value),
            IssuePairingOutcome.LicenseInactive => Failure(StatusCodes.Status403Forbidden, ApiCodes.LicenseSalesStopped, _text["License_SalesStopped"].Value),
            _ => ValidationFailure("name", _text["Api_ValidationFailed"].Value),
        };
    }

    /// <summary>Removes a till (or cancels an unused code) and frees its seat. The till is refused from its next request on.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.License_Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "removeTill")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
        => await _tills.RevokeAsync(id, ct) ? NoContent() : Failure(StatusCodes.Status404NotFound, ApiCodes.NotFound);

    /// <summary>
    /// Called by a till (no sign-in needed): exchanges a pairing code for the device cookie. 400 <c>invalid_pairing_code</c> for a wrong,
    /// used or expired code; 429 <c>rate_limited</c> after too many wrong ones; 409 <c>seats_full</c> if the licence has shrunk since the code was made.
    /// </summary>
    [HttpPost("pair")]
    [AllowAnonymous]
    [ProducesResponseType<PairedDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "pairTill")]
    public async Task<ActionResult<PairedDto>> Pair(PairRequest request, CancellationToken ct)
    {
        var clientKey = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local";
        var result = await _tills.PairAsync(request.Code, clientKey, ct);

        switch (result.Outcome)
        {
            case PairOutcome.Success:
                // A long-lived, script-proof cookie: the till keeps selling after the browser is closed and reopened.
                Response.Cookies.Append(TillGate.CookieName, result.Token!, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps,
                    IsEssential = true,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddYears(5),
                });
                return new PairedDto { Name = result.Name! };
            case PairOutcome.TooManyAttempts:
                return Failure(StatusCodes.Status429TooManyRequests, ApiCodes.RateLimited, _text["License_PairingThrottled"].Value);
            case PairOutcome.SeatsFull:
                return ConflictFailure(ApiCodes.SeatsFull, _text["License_SeatsFull"].Value);
            case PairOutcome.LicenseInactive:
                return Failure(StatusCodes.Status403Forbidden, ApiCodes.LicenseSalesStopped, _text["License_SalesStopped"].Value);
            default:
                return Failure(StatusCodes.Status400BadRequest, ApiCodes.InvalidPairingCode, _text["License_InvalidPairingCode"].Value);
        }
    }

    /// <summary>What this PC is: the store server, an allowed till, or not allowed to sell. Public, so the pairing screen can decide what to show.</summary>
    [HttpGet("me")]
    [AllowAnonymous]
    [ProducesResponseType<DeviceStatusDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getDeviceStatus")]
    public async Task<DeviceStatusDto> Me() => new()
    {
        IsServer = TillGate.IsServerPc(HttpContext),
        Access = (await TillGate.CheckAsync(HttpContext)).ToString(),
    };

    private static TillSeatsDto ToDto(TillSeatsInfo info) => new()
    {
        Seats = info.Seats,
        TillSeats = info.TillSeats == int.MaxValue ? null : info.TillSeats,
        Used = info.Used,
        Enforced = info.Enforced,
        Devices = info.Devices.Select(d => new TillDeviceDto
        {
            Id = d.Id,
            Name = d.Name,
            State = d.State.ToString(),
            CreatedAt = d.CreatedAt,
            PairedAt = d.PairedAt,
            LastSeenAt = d.LastSeenAt,
            PairingExpiresAt = d.PairingExpiresAt,
            Overflow = d.Overflow,
        }).ToList(),
    };
}
