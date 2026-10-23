using Application.IServices.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

/// <summary>
/// The License page: shows the license status and this computer's Machine ID, and accepts a pasted
/// license key. Anonymous on purpose: it has to be reachable when the POS is stopped (nobody may be
/// able to log in on a fresh install), and a key only activates if it was signed for this exact PC,
/// so letting anyone at the keyboard paste one is harmless.
/// </summary>
[AllowAnonymous]
public class LicenseController : BaseController
{
    private readonly ILicenseService _license;

    public LicenseController(ILicenseService license)
    {
        _license = license;
    }

    [HttpGet]
    public IActionResult Index() => View(_license.GetState());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(16 * 1024)]
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
