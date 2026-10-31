using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Application.IServices.Auth;
using Application.IServices.Users;
using Core.Auditing;
using Core.Entities.Logging;
using Web.Models.Auth;
using Web.Helpers;
using Web.Services;
using System.Security.Claims;

namespace Web.Controllers;

public class AccountController : BaseController
{
    private readonly IAuthService _authService;
    private readonly IUserService _userService;
    private readonly ILogger<AccountController> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IAuditTrail _auditTrail;
    private readonly IRoleAccessService _roleAccess;

    public AccountController(IAuthService authService, IUserService userService, ILogger<AccountController> logger, IStringLocalizer<SharedResource> localizer, IAuditTrail auditTrail, IRoleAccessService roleAccess)
    {
        _roleAccess = roleAccess;
        _localizer = localizer;
        _authService = authService;
        _userService = userService;
        _logger = logger;
        _auditTrail = auditTrail;
    }

    // Cookie that remembers the username of the last user to sign in on this
    // machine, so daily logins only need a PIN. Not HttpOnly so the "Not you?"
    // link on the login page can clear it client-side.
    private const string LastUsernameCookie = "erp_last_username";

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        // If user is already authenticated, redirect to return URL or dashboard
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect(returnUrl ?? "/");
        }

        var model = new LoginViewModel
        {
            ReturnUrl = returnUrl
        };

        if (Request.Cookies.TryGetValue(LastUsernameCookie, out var lastUsername) && !string.IsNullOrWhiteSpace(lastUsername))
        {
            model.Username = lastUsername;
        }

        await LoadSignInUsersAsync(model);
        return View(model);
    }

    // Fills the name picker. The remembered user stays selected while they're still active (the page then
    // opens straight on their PIN pad); otherwise nobody is selected and the page opens on the picker,
    // unless there's only one active user to choose from.
    private async Task LoadSignInUsersAsync(LoginViewModel model)
    {
        var users = await _userService.GetAllAsync();
        model.Users = users
            .Where(u => u.IsActive)
            .Select(u => new { u.Username, Name = u.GetFullName() })
            .OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new SignInUserViewModel(u.Username, u.Name, InitialsOf(u.Name)))
            .ToList();

        if (!model.Users.Any(u => string.Equals(u.Username, model.Username, StringComparison.OrdinalIgnoreCase)))
        {
            model.Username = model.Users.Count == 1 ? model.Users[0].Username : string.Empty;
        }
    }

    private static string InitialsOf(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ModelState.Remove(nameof(model.Pin));
            model.Pin = string.Empty;
            await LoadSignInUsersAsync(model);
            return View(model);
        }

        try
        {
            var result = await _authService.LoginWithPinAsync(model.Username, model.Pin);

            if (result.IsSuccess && !string.IsNullOrEmpty(result.Token))
            {
                // The user's grants come from the database (managed on the Users page). A user whose access
                // can't be resolved gets none, rather than falling back to some default role.
                var access = result.User is null ? null : await _roleAccess.GetUserAccessAsync(result.User.Id);
                var permissions = access?.Permissions ?? Array.Empty<string>();

                // Set authentication cookie for web interface
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, result.User?.Username ?? model.Username),
                    new Claim(ClaimTypes.NameIdentifier, result.User?.Id.ToString() ?? "0"),
                    new Claim(ClaimTypes.Email, result.User?.Email ?? ""),
                    new Claim("FullName", result.User?.GetFullName() ?? ""),
                    new Claim("Role", result.User?.Role?.Name ?? ""),
                    // Also emit the framework role claim so User.IsInRole(...) and [RequireRole] work.
                    new Claim(ClaimTypes.Role, result.User?.Role?.Name ?? ""),
                    new Claim("RoleId", result.User?.RoleId.ToString() ?? "0")
                };

                // Add all permissions for the user's role
                foreach (var permission in permissions)
                {
                    claims.Add(new Claim("Permission", permission));
                }

                var claimsIdentity = new ClaimsIdentity(claims, "Cookies");
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
                };

                await HttpContext.SignInAsync("Cookies", new ClaimsPrincipal(claimsIdentity), authProperties);

                // Remember the username for the next login on this machine.
                Response.Cookies.Append(LastUsernameCookie, result.User?.Username ?? model.Username, new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    HttpOnly = false,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax
                });

        var currentLang = CurrentLanguage;

        if (result.User?.MustChangePin == true)
        {
            return RedirectToAction(nameof(ChangePin), new { lang = currentLang });
        }

        // Redirect to return URL or dashboard with language
        if (!string.IsNullOrEmpty(model.ReturnUrl))
        {
            // Ensure return URL has language prefix
            var returnUrl = model.ReturnUrl;
            if (!returnUrl.StartsWith($"/{currentLang}/"))
            {
                returnUrl = $"/{currentLang}{returnUrl}";
            }
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "POS", new { lang = currentLang });
            }

            ModelState.AddModelError(string.Empty, _localizer[result.ErrorMessage ?? "Invalid username or PIN"].Value);
            ModelState.Remove(nameof(model.Pin));
            model.Pin = string.Empty;
            await LoadSignInUsersAsync(model);
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error for user: {Username}", model.Username);
            ModelState.AddModelError(string.Empty, _localizer["An error occurred during login. Please try again."].Value);
            ModelState.Remove(nameof(model.Pin));
            model.Pin = string.Empty;
            await LoadSignInUsersAsync(model);
            return View(model);
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        // Who signed out is taken from the still-authenticated request.
        _auditTrail.LogSecurity(AuditActions.Logout, $"'{User.Identity?.Name}' signed out", details: new { method = "web" });
        await HttpContext.SignOutAsync("Cookies");
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [Authorize]
    public IActionResult Profile()
    {
        var userInfo = new
        {
            Id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Username = User.FindFirst(ClaimTypes.Name)?.Value,
            Email = User.FindFirst(ClaimTypes.Email)?.Value,
            FullName = User.FindFirst("FullName")?.Value,
            Role = User.FindFirst("Role")?.Value,
            RoleId = User.FindFirst("RoleId")?.Value
        };

        ViewData["Title"] = _localizer["Profile"].Value;
        ViewData["Subtitle"] = _localizer["Prof_Subtitle"].Value;
        return View(userInfo);
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        ViewData["Title"] = _localizer["ChangePin"].Value;
        ViewData["Subtitle"] = _localizer["Pin_UpdateYourLogin"].Value;
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        ViewData["Title"] = _localizer["ChangePin"].Value;
        ViewData["Subtitle"] = _localizer["Pin_UpdateYourLogin"].Value;

        if (model.NewPin != model.ConfirmPin)
        {
            ModelState.AddModelError(nameof(model.ConfirmPin), _localizer["PINs do not match"].Value);
        }

        if (!ModelState.IsValid)
        {
            ClearPins(model);
            return View(model);
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        try
        {
            var user = await _userService.GetByIdAsync(userId);
            if (user == null || !await _userService.ValidatePinAsync(user, model.CurrentPin))
            {
                // Someone at a signed-in screen guessing the PIN is worth knowing about.
                _auditTrail.LogSecurity(AuditActions.PinChangeFailed, "PIN change refused: the current PIN was incorrect",
                    AuditOutcome.Failure, AuditLogSeverity.Warning, entityId: userId, entityLabel: User.Identity?.Name);
                ModelState.AddModelError(nameof(model.CurrentPin), _localizer["Current PIN is incorrect"].Value);
                ClearPins(model);
                return View(model);
            }

            if (model.NewPin == model.CurrentPin)
            {
                ModelState.AddModelError(nameof(model.NewPin), _localizer["New PIN must be different from your current PIN"].Value);
                ClearPins(model);
                return View(model);
            }

            await _userService.SetPinAsync(userId, model.NewPin, requireChangeOnNextLogin: false);

            TempData["SuccessMessage"] = "PIN updated successfully!";
            return RedirectToAction("Profile", "Account", new { lang = CurrentLanguage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PIN change error for user: {UserId}", userId);
            ModelState.AddModelError(string.Empty, _localizer["An error occurred while updating your PIN. Please try again."].Value);
        }

        ClearPins(model);
        return View(model);
    }

    // Tag helpers render values from ModelState before the model, so the posted values
    // must be overwritten there too (SetModelValue keeps the validation errors intact).
    private void ClearPins(ChangePasswordViewModel model)
    {
        model.CurrentPin = string.Empty;
        model.NewPin = string.Empty;
        model.ConfirmPin = string.Empty;
        ResetPinModelState(nameof(model.CurrentPin), nameof(model.NewPin), nameof(model.ConfirmPin));
    }

    private void ClearPins(ChangePinViewModel model)
    {
        model.NewPin = string.Empty;
        model.ConfirmPin = string.Empty;
        ResetPinModelState(nameof(model.NewPin), nameof(model.ConfirmPin));
    }

    private void ResetPinModelState(params string[] keys)
    {
        foreach (var key in keys)
        {
            ModelState.SetModelValue(key, string.Empty, string.Empty);
        }
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePin()
    {
        ViewData["Title"] = _localizer["ChangePin"].Value;
        ViewData["Subtitle"] = _localizer["Pin_SetNew"].Value;
        return View(new ChangePinViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangePin(ChangePinViewModel model)
    {
        ViewData["Title"] = _localizer["ChangePin"].Value;
        ViewData["Subtitle"] = _localizer["Pin_SetNew"].Value;

        if (model.NewPin != model.ConfirmPin)
        {
            ModelState.AddModelError(nameof(model.ConfirmPin), _localizer["PINs do not match"].Value);
        }

        if (!ModelState.IsValid)
        {
            ClearPins(model);
            return View(model);
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        var user = await _userService.GetByIdAsync(userId);
        if (user != null && await _userService.ValidatePinAsync(user, model.NewPin))
        {
            ModelState.AddModelError(nameof(model.NewPin), _localizer["Please choose a PIN different from your current one"].Value);
            ClearPins(model);
            return View(model);
        }

        await _userService.SetPinAsync(userId, model.NewPin, requireChangeOnNextLogin: false);

        TempData["SuccessMessage"] = "PIN updated successfully!";
        return RedirectToAction("Index", "POS", new { lang = CurrentLanguage });
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        ViewData["Title"] = "Access Denied";
        ViewData["Subtitle"] = "You don't have permission to access this resource";
        return View();
    }
}
