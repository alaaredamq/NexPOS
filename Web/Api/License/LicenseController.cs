using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.License.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.License;

/// <summary>
/// The licence screen: status, this PC's Machine ID, and activation of a pasted key. Administrators only
/// (<c>License.Manage</c>, as on the Razor page).
/// <para>
/// Deliberately not behind <c>[RequireSalesLicense]</c>: an unlicensed install only stops <em>selling</em>, so an administrator
/// can always sign in, open this screen and fix it. A cashier bounced off the POS never needs this controller - the shell shows
/// them the "ask your administrator" screen from the session's licence summary.
/// </para>
/// </summary>
[Route("api/v1/license")]
[RequirePermission(PermissionHelper.Permissions.License_Manage)]
public sealed class LicenseController : ApiControllerBase
{
    private readonly ILicenseApiService _license;

    public LicenseController(ILicenseApiService license) => _license = license;

    /// <summary>What is installed and what it allows.</summary>
    [HttpGet]
    [ProducesResponseType<LicenseDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getLicense")]
    public LicenseDto Get() => _license.Get();

    /// <summary>
    /// Installs a pasted key. A refused key (incomplete, forged, for another computer, older than the active one ...) answers 400
    /// <c>validation_failed</c> with the reason on <c>licenseKey</c>, and nothing changes. On success the answer is the new state;
    /// the shell's banner follows on its next refresh of the session.
    /// </summary>
    [HttpPost("activate")]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType<LicenseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "activateLicense")]
    public ActionResult<LicenseDto> Activate(LicenseActivateRequest request)
    {
        try
        {
            return _license.Activate(request.LicenseKey);
        }
        catch (LicenseActivationException ex)
        {
            return ValidationFailure("licenseKey", ex.Message);
        }
    }
}
