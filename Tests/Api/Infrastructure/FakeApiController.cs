using System.ComponentModel.DataAnnotations;
using Application.DTOs.Licensing;
using Application.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Tests.Api.Infrastructure;

/// <summary>Stands in for a module: one endpoint per convention the infrastructure promises. Only exists in the test host.</summary>
[Route("api/v1/fake")]
public sealed class FakeApiController : ApiControllerBase
{
    [HttpGet("open")]
    [AllowAnonymous]
    public IActionResult Open() => Ok(new { ok = true });

    [HttpGet("signed-in")]
    [AnySignedInUser]
    public IActionResult SignedIn() => Ok(new { ok = true, user = User.Identity?.Name });

    [HttpGet("users-only")]
    [RequirePermission(PermissionHelper.Permissions.Users_View)]
    public IActionResult UsersOnly() => Ok(new { ok = true });

    [HttpGet("paged")]
    [AnySignedInUser]
    public PagedResult<int> Paged([FromQuery] PagedQuery query) => PagedResult<int>.From(Enumerable.Range(1, 95), query);

    [HttpPost("echo")]
    [AnySignedInUser]
    public IActionResult Echo(EchoRequest request) => Ok(request);

    [HttpPost("echo-nothing")]
    [AnySignedInUser]
    public IActionResult EchoNothing() => Ok();

    [HttpGet("throws")]
    [AnySignedInUser]
    public IActionResult Throws() => throw new InvalidOperationException("secret detail that a shop owner must not see");

    [HttpGet("license-exception")]
    [AnySignedInUser]
    public IActionResult LicenseException() => throw new LicenseRequiredException(LicenseStatus.Expired);

    [HttpGet("sales-only")]
    [AnySignedInUser]
    [RequireSalesLicense]
    public IActionResult SalesOnly() => Ok(new { ok = true });

    [HttpGet("missing")]
    [AnySignedInUser]
    public IActionResult Missing() => NotFound();

    [HttpGet("drawer")]
    [AnySignedInUser]
    public IActionResult Drawer() => ConflictFailure("drawer_not_open", "Open the drawer first.");

    [HttpGet("field")]
    [AnySignedInUser]
    public IActionResult Field() => ValidationFailure("OpeningCash", "Must not be negative.");

    [HttpGet("language")]
    [AnySignedInUser]
    public IActionResult Language() => Ok(new { language = CurrentLanguage });
}

public sealed class EchoRequest
{
    [Required(ErrorMessage = "Username is required")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 10)]
    public int Quantity { get; set; } = 1;

    public List<EchoLine> Lines { get; set; } = new();
}

public sealed class EchoLine
{
    [Range(1, 100)]
    public int UnitPrice { get; set; } = 1;
}
