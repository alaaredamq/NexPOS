using Application.IServices.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

/// <summary>
/// The License page: shows the license status and this computer's Machine ID, and accepts a pasted
/// license key. Administrators only (<see cref="PermissionHelper.Permissions.License_Manage"/>).
/// An unlicensed install only stops *selling* (see LicenseEnforcementMiddleware) - signing in, settings
/// and backups keep working - so an administrator can always get here after logging in.
/// A cashier who is bounced off the POS (<c>?from=pos</c>) gets a short "ask your administrator"
/// screen instead of a dead-end AccessDenied.
/// </summary>
[Authorize]
public class LicenseController : BaseController
{
    private readonly ILicenseService _license;

    public LicenseController(ILicenseService license)
    {
        _license = license;
    }

    [HttpGet]
    public IActionResult Index(string? from = null)
    {
        if (!PermissionHelper.HasPermission(User, PermissionHelper.Permissions.License_Manage))
        {
            return string.Equals(from, "pos", StringComparison.OrdinalIgnoreCase)
                ? View("SalesStopped")
                : RedirectToAction("AccessDenied", "Account");
        }

        return View(_license.GetState());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(16 * 1024)]
    [RequirePermission(PermissionHelper.Permissions.License_Manage)]
    public IActionResult Activate(string? licenseKey)
    {
        var result = _license.Activate(licenseKey ?? string.Empty);

        if (result.Success)
            TempData["LicenseSuccess"] = true;
        else
            TempData["LicenseError"] = result.Error.ToString();

        return Redirect($"/{CurrentLanguage}/License");
    }
}
