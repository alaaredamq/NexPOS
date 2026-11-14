using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Services;

namespace Web.Controllers;

/// <summary>
/// Serves the white-label logo and favicon: the uploaded file when there is one, otherwise the
/// built-in Coduo POS image. Anonymous because the login and loading screens need them too.
/// </summary>
[AllowAnonymous]
public class BrandingController : Controller
{
    private readonly IBrandingService _branding;
    private readonly IWebHostEnvironment _env;

    public BrandingController(IBrandingService branding, IWebHostEnvironment env)
    {
        _branding = branding;
        _env = env;
    }

    /// <param name="variant">
    /// Which built-in logo to fall back to: "full" (sidebar), "light" (dark header bars) or
    /// "mark" (square icon). An uploaded logo is used as-is for every variant.
    /// </param>
    [HttpGet("branding/logo")]
    public IActionResult Logo(string? variant = null)
    {
        var custom = _branding.GetLogoFile();
        if (custom != null) return Image(custom.Value.Path, custom.Value.ContentType);

        var fallback = variant switch
        {
            "light" => "images/logo-light.svg",
            "mark" => "images/logo-mark.svg",
            "icon" => "images/logo-icon-256.png",
            _ => "images/logo.svg",
        };
        return Image(Path.Combine(_env.WebRootPath, fallback), ContentTypeOf(fallback));
    }

    [HttpGet("branding/favicon")]
    public IActionResult Favicon()
    {
        var custom = _branding.GetFaviconFile();
        if (custom != null) return Image(custom.Value.Path, custom.Value.ContentType);

        return Image(Path.Combine(_env.WebRootPath, "favicon.svg"), "image/svg+xml");
    }

    private IActionResult Image(string path, string contentType)
    {
        if (!System.IO.File.Exists(path)) return NotFound();

        // Uploaded SVGs can carry script; if one is ever opened directly rather than through
        // an <img>, the sandbox policy stops it running in the app's origin.
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Callers add ?v=<version>, which changes whenever the branding does.
        Response.Headers["Cache-Control"] = "public,max-age=86400";
        return PhysicalFile(path, contentType);
    }

    private static string ContentTypeOf(string path) =>
        Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png";
}
