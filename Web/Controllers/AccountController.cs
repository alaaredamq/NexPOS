using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Application.IServices.Auth;
using Application.IServices.Users;
using Web.Models.Auth;
using Web.Helpers;
using System.Security.Claims;

namespace Web.Controllers;

public class AccountController : BaseController
{
    private readonly IAuthService _authService;
    private readonly IUserService _userService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAuthService authService, IUserService userService, ILogger<AccountController> logger)
    {
        _authService = authService;
        _userService = userService;
        _logger = logger;
    }

    // Cookie that remembers the username of the last non-admin user to sign in on this
    // machine, so daily cashier logins only need a PIN. Not HttpOnly so the "Not you?"
    // link on the login page can clear it client-side.
    private const string LastUsernameCookie = "erp_last_username";

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
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

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Pin = string.Empty;
            return View(model);
        }

        try
        {
            var result = await _authService.LoginWithPinAsync(model.Username, model.Pin);

            if (result.IsSuccess && !string.IsNullOrEmpty(result.Token))
            {
                // Get permissions for the user's role. Fall back to the least-privileged role
                // rather than Admin if the role somehow failed to load.
                var userRole = result.User?.Role?.Name ?? PermissionHelper.Roles.Viewer;
                var permissions = PermissionHelper.GetPermissionsForRole(userRole);
                
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

                // Remember the username for the next login on this machine (skip the admin account).
                if (!string.Equals(model.Username, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    Response.Cookies.Append(LastUsernameCookie, model.Username, new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddYears(1),
                        HttpOnly = false,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax
                    });
                }

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

            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Invalid username or PIN");
            model.Pin = string.Empty;
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error for user: {Username}", model.Username);
            ModelState.AddModelError(string.Empty, "An error occurred during login. Please try again.");
            model.Pin = string.Empty;
            return View(model);
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
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

        ViewData["Title"] = "Profile";
        ViewData["Subtitle"] = "Your account information";
        return View(userInfo);
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        ViewData["Title"] = "Change PIN";
        ViewData["Subtitle"] = "Update your login PIN";
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        ViewData["Title"] = "Change PIN";
        ViewData["Subtitle"] = "Update your login PIN";

        if (model.NewPin != model.ConfirmPin)
        {
            ModelState.AddModelError(nameof(model.ConfirmPin), "PINs do not match");
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
                ModelState.AddModelError(nameof(model.CurrentPin), "Current PIN is incorrect");
                ClearPins(model);
                return View(model);
            }

            if (model.NewPin == model.CurrentPin)
            {
                ModelState.AddModelError(nameof(model.NewPin), "New PIN must be different from your current PIN");
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
            ModelState.AddModelError(string.Empty, "An error occurred while updating your PIN. Please try again.");
        }

        ClearPins(model);
        return View(model);
    }

    private static void ClearPins(ChangePasswordViewModel model)
    {
        model.CurrentPin = string.Empty;
        model.NewPin = string.Empty;
        model.ConfirmPin = string.Empty;
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePin()
    {
        ViewData["Title"] = "Change PIN";
        ViewData["Subtitle"] = "Set a new PIN for logging in";
        return View(new ChangePinViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangePin(ChangePinViewModel model)
    {
        ViewData["Title"] = "Change PIN";
        ViewData["Subtitle"] = "Set a new PIN for logging in";

        if (model.NewPin != model.ConfirmPin)
        {
            ModelState.AddModelError(nameof(model.ConfirmPin), "PINs do not match");
        }

        if (!ModelState.IsValid)
        {
            model.NewPin = string.Empty;
            model.ConfirmPin = string.Empty;
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
            ModelState.AddModelError(nameof(model.NewPin), "Please choose a PIN different from your current one");
            model.NewPin = string.Empty;
            model.ConfirmPin = string.Empty;
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
