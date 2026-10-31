using System.Security.Claims;
using Core.Entities.Users;

namespace Web.Api.Auth;

/// <summary>
/// The claims of a signed-in cookie. Identical to what <c>AccountController.Login</c> issues, because the Razor pages
/// and the SPA share the cookie and <c>OnValidatePrincipal</c> / <c>[RequirePermission]</c> read these exact claim types.
/// </summary>
internal static class SignInPrincipal
{
    public static ClaimsPrincipal Create(User user, IEnumerable<string> permissions, string typedUsername)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username ?? typedUsername),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? ""),
            new("FullName", user.GetFullName()),
            new("Role", user.Role?.Name ?? ""),
            // Also the framework role claim so User.IsInRole(...) and [RequireRole] work.
            new(ClaimTypes.Role, user.Role?.Name ?? ""),
            new("RoleId", user.RoleId.ToString()),
        };
        claims.AddRange(permissions.Select(p => new Claim("Permission", p)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
    }
}
