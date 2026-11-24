using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Cloud.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sync;

namespace Cloud.Services;

/// <summary>Admin app tokens: 256 random bits behind <c>nxa_</c>; only a SHA-256 is stored.</summary>
public static class AdminTokens
{
    public const string Scheme = "nxa_";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public static (string Token, string Hash) Generate()
    {
        var token = Scheme + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("admin:" + token)));

    /// <summary>The token in <c>Authorization: Bearer nxa_...</c>, or null.</summary>
    public static string? FromHeader(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header["Bearer ".Length..].Trim();
        return token.StartsWith(Scheme, StringComparison.Ordinal) ? token : null;
    }
}

/// <summary>
/// Checks an admin's name and password, for the portal's sign-in page and the admin app alike. Failures are throttled per address
/// AND per username, so neither guessing many names from one machine nor one name from many machines gets unlimited tries.
/// </summary>
public sealed class AdminSignIn(ControlDbContext db, LoginThrottle throttle)
{
    public enum Outcome { Ok, Wrong, Locked }

    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash("not-a-real-password"));

    public async Task<(Outcome Outcome, AdminUser? Admin)> CheckAsync(string? username, string? password, string? address)
    {
        var name = (username ?? "").Trim();
        var keys = new[] { "ip:" + (address ?? "unknown"), "user:" + name.ToLowerInvariant() };
        if (keys.Any(throttle.IsBlocked)) return (Outcome.Locked, null);

        var admin = name.Length == 0 ? null : await db.AdminUsers.FirstOrDefaultAsync(a => a.Username == name);
        // Verify even for an unknown user so response time does not reveal which usernames exist.
        var ok = PasswordHasher.Verify(password ?? "", admin?.PasswordHash ?? DummyHash.Value);
        if (admin is null || !ok)
        {
            foreach (var key in keys) throttle.RecordFailure(key);
            return (Outcome.Wrong, null);
        }
        foreach (var key in keys) throttle.Reset(key);
        return (Outcome.Ok, admin);
    }
}

/// <summary>
/// The "AdminToken" authentication scheme: who is calling /api/admin/v1. It reads only the bearer token, never the portal's cookie,
/// so a page on another site cannot make a signed-in browser call the API (there is nothing for it to ride on).
/// </summary>
public sealed class AdminTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    ControlDbContext db, TimeProvider time) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AdminToken";
    public const string SessionClaim = "admin_session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = AdminTokens.FromHeader(Request);
        if (token is null) return AuthenticateResult.NoResult();

        var hash = AdminTokens.Hash(token);
        var now = time.GetUtcNow().UtcDateTime;
        var session = await db.AdminSessions.Include(s => s.AdminUser).FirstOrDefaultAsync(s => s.TokenHash == hash);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
            return AuthenticateResult.Fail("Unknown, expired or signed-out token.");

        // Cheap enough once a minute; tells the provider which signed-in device is actually in use.
        if (session.LastUsedAt is null || now - session.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            session.LastUsedAt = now;
            await db.SaveChangesAsync();
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, session.AdminUser.Username),
            new Claim(ClaimTypes.NameIdentifier, session.AdminUserId.ToString()),
            new Claim(SessionClaim, session.Id.ToString()),
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>A JSON 401 the app can act on, never a redirect to the portal's sign-in page.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new SyncError(SyncErrorCodes.Unauthorized, "Please sign in again."), SyncWire.Json);
    }
}
