using Application.IServices.Auth;
using Application.IServices.Users;
using Core.Auditing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Auth.Contracts;
using Web.Api.Infrastructure;
using Web.Api.Session;
using Web.Api.Session.Contracts;
using Web.Services;

namespace Web.Api.Auth;

/// <summary>Sign in and out with the shop's 4-digit PIN. The session is an HttpOnly cookie shared with the Razor pages.</summary>
[Route("api/v1/auth")]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Remembers the last user to sign in on this PC so the daily sign-in is just a PIN. Same cookie the Razor login uses.</summary>
    private const string LastUsernameCookie = "erp_last_username";

    // AuthService words its refusals in English; the message is the only thing it returns. Matches AccountController.
    private const string DisabledMessage = "Account is disabled";

    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private readonly IRoleAccessService _roleAccess;
    private readonly ISessionBootstrapService _bootstrap;
    private readonly IAuditTrail _audit;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public AuthController(
        IAuthService auth, IUserService users, IRoleAccessService roleAccess, ISessionBootstrapService bootstrap,
        IAuditTrail audit, IStringLocalizer<SharedResource> localizer)
    {
        _auth = auth;
        _users = users;
        _roleAccess = roleAccess;
        _bootstrap = bootstrap;
        _audit = audit;
        _localizer = localizer;
    }

    /// <summary>The name picker of the login screen: active accounts and the last user to sign in here. Public, as the Razor login page is.</summary>
    [HttpGet("sign-in-options")]
    [AllowAnonymous]
    [ProducesResponseType<SignInOptionsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getSignInOptions")]
    public async Task<ActionResult<SignInOptionsDto>> SignInOptions()
    {
        var people = (await _users.GetAllAsync())
            .Where(u => u.IsActive)
            .Select(u => new { u.Username, Name = u.GetFullName() })
            .OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new SignInUserDto { Username = u.Username, DisplayName = u.Name, Initials = SessionBootstrapService.InitialsOf(u.Name) })
            .ToList();

        var last = Request.Cookies.TryGetValue(LastUsernameCookie, out var remembered) ? remembered : null;
        var lastUsername = people.FirstOrDefault(p => string.Equals(p.Username, last, StringComparison.OrdinalIgnoreCase))?.Username
            ?? (people.Count == 1 ? people[0].Username : string.Empty);

        return new SignInOptionsDto { Users = people, LastUsername = lastUsername };
    }

    /// <summary>Signs in with username + PIN and returns the session document.</summary>
    /// <remarks>
    /// The response is the same as <c>GET /api/v1/session</c>, so the shell can draw itself without another round trip. Wrong credentials answer 401 <c>invalid_credentials</c> (the same message for an
    /// unknown user and a wrong PIN); a disabled account answers 401 <c>account_disabled</c>. Throttled per client IP.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status401Unauthorized, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "login")]
    public async Task<ActionResult<SessionDto>> Login(LoginRequest request)
    {
        var result = await _auth.LoginWithPinAsync(request.Username, request.Pin);
        if (!result.IsSuccess || result.User is null)
        {
            var message = _localizer[result.ErrorMessage ?? "Invalid username or PIN"].Value;
            var code = result.ErrorMessage == DisabledMessage ? ApiCodes.AccountDisabled : ApiCodes.InvalidCredentials;
            return Failure(StatusCodes.Status401Unauthorized, code, message);
        }

        // The user's grants come from the database; a user whose access cannot be resolved gets none rather than a default role.
        var access = await _roleAccess.GetUserAccessAsync(result.User.Id);
        var principal = SignInPrincipal.Create(result.User, access?.Permissions ?? Array.Empty<string>(), request.Username);

        await HttpContext.SignInAsync("Cookies", principal, new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            ExpiresUtc = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8),
        });

        Response.Cookies.Append(LastUsernameCookie, result.User.Username, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = false, // the login screen's "Not you?" link clears it client-side
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
        });

        return await _bootstrap.BuildAsync(principal, CurrentLanguage, result.User.MustChangePin);
    }

    /// <summary>Ends the session. Always 204, signed in or not, so a stale tab can sign out without error.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SwaggerOperation(OperationId = "logout")]
    public async Task<IActionResult> Logout()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            _audit.LogSecurity(AuditActions.Logout, $"'{User.Identity.Name}' signed out", details: new { method = "web" });
        }

        await HttpContext.SignOutAsync("Cookies");
        return NoContent();
    }
}
