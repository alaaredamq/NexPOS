using Application.IServices.Auth;
using Application.IServices.Users;
using Application.Models;
using Core.Auditing;
using Core.Entities.Logging;
using Core.Entities.Users;
using Core.IRepositories.Users;

namespace Application.Services.Auth;

public class AuthService : IAuthService
{
    private readonly IUserService _userService;
    private readonly IJwtService _jwtService;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IAuditTrail _auditTrail;
    private readonly IAuditContext _auditContext;

    public AuthService(
        IUserService userService, IJwtService jwtService, IUserSessionRepository sessionRepository,
        IAuditTrail auditTrail, IAuditContext auditContext)
    {
        _userService = userService;
        _jwtService = jwtService;
        _sessionRepository = sessionRepository;
        _auditTrail = auditTrail;
        _auditContext = auditContext;
    }

    /// <summary>
    /// Records a refused sign-in with its precise reason. The reason is for support only - the caller still
    /// gets the same generic message for an unknown user and a wrong PIN, so the form leaks nothing.
    /// </summary>
    private LoginResult RefuseLogin(string method, string attemptedUsername, User? user, string reason, string message)
    {
        _auditTrail.LogSecurity(AuditActions.LoginFailed,
            $"Sign-in refused for '{attemptedUsername}': {reason}",
            AuditOutcome.Failure, AuditLogSeverity.Warning,
            entityId: user?.Id, entityLabel: attemptedUsername,
            details: new { method, reason },
            userId: user?.Id, username: attemptedUsername);

        return LoginResult.Failure(message);
    }

    /// <summary>
    /// From here on the request is the user's. The last-login update and the session row are written before the
    /// auth cookie exists, so without this they would be filed under "nobody".
    /// </summary>
    private void AttributeToUser(User user) => _auditContext.SetActor(user.Id, user.Username, user.Role?.Name);

    /// <summary>Only called once the session exists, so the trail never claims a sign-in that failed half-way.</summary>
    private void RecordSignIn(User user, string method)
        => _auditTrail.LogSecurity(AuditActions.Login,
            $"'{user.Username}' signed in",
            entityId: user.Id, entityLabel: user.Username,
            details: new { method, mustChangePin = user.MustChangePin, mustChangePassword = user.MustChangePassword },
            userId: user.Id, username: user.Username);

    public async Task<LoginResult> LoginAsync(string username, string password, string? ipAddress = null, string? userAgent = null)
    {
        var user = await _userService.GetByUsernameAsync(username);
        if (user == null)
            return RefuseLogin("password", username, null, "unknown user", "Invalid username or password");

        if (!user.IsActive)
            return RefuseLogin("password", username, user, "account is disabled", "Account is disabled");

        if (!await _userService.ValidatePasswordAsync(user, password))
            return RefuseLogin("password", username, user, "wrong password", "Invalid username or password");

        AttributeToUser(user);

        // Update last login
        user.UpdateLastLogin();
        await _userService.UpdateAsync(user.Id, user);

        // Generate tokens
        var accessToken = _jwtService.GenerateAccessToken(user);
        var sessionToken = _jwtService.GenerateSessionToken();
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Create session
        var session = new UserSession(
            user.Id,
            sessionToken,
            DateTime.UtcNow.AddHours(24), // 24 hour session
            ipAddress,
            userAgent,
            refreshToken
        );

        await _sessionRepository.SaveAsync(session);

        RecordSignIn(user, "password");
        return LoginResult.Success(accessToken, sessionToken, refreshToken, user);
    }

    public async Task<LoginResult> LoginWithPinAsync(string username, string pin, string? ipAddress = null, string? userAgent = null)
    {
        var user = await _userService.GetByUsernameAsync(username);
        if (user == null)
            return RefuseLogin("pin", username, null, "unknown user", "Invalid username or PIN");

        if (!user.IsActive)
            return RefuseLogin("pin", username, user, "account is disabled", "Account is disabled");

        if (!await _userService.ValidatePinAsync(user, pin))
            return RefuseLogin("pin", username, user, user.PinHash == null ? "no PIN set for this user" : "wrong PIN", "Invalid username or PIN");

        AttributeToUser(user);

        // Update last login
        user.UpdateLastLogin();
        await _userService.UpdateAsync(user.Id, user);

        // Generate tokens
        var accessToken = _jwtService.GenerateAccessToken(user);
        var sessionToken = _jwtService.GenerateSessionToken();
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Create session
        var session = new UserSession(
            user.Id,
            sessionToken,
            DateTime.UtcNow.AddHours(24), // 24 hour session
            ipAddress,
            userAgent,
            refreshToken
        );

        await _sessionRepository.SaveAsync(session);

        RecordSignIn(user, "pin");
        return LoginResult.Success(accessToken, sessionToken, refreshToken, user);
    }

    public async Task LogoutAsync(string sessionToken)
    {
        var session = await _sessionRepository.GetBySessionTokenAsync(sessionToken);
        if (session != null)
        {
            session.Deactivate();
            await _sessionRepository.SaveAsync(session);
            _auditTrail.LogSecurity(AuditActions.Logout, "Session ended (API sign-out)",
                entityId: session.UserId, userId: session.UserId, details: new { sessionId = session.Id });
        }
    }

    public async Task<UserSession?> ValidateSessionAsync(string sessionToken)
    {
        var session = await _sessionRepository.GetBySessionTokenAsync(sessionToken);
        
        if (session == null || !session.IsActive || session.IsExpired())
            return null;

        session.UpdateLastAccess();
        await _sessionRepository.SaveAsync(session);
        
        return session;
    }

    public async Task<User?> GetCurrentUserAsync(string sessionToken)
    {
        var session = await ValidateSessionAsync(sessionToken);
        if (session == null)
            return null;

        return await _userService.GetByIdAsync(session.UserId);
    }

    public async Task<bool> HasPermissionAsync(int userId, string permission)
    {
        var user = await _userService.GetByIdAsync(userId);
        if (user == null) return false;

        // Check role permissions
        var hasRolePermission = user.Role.RolePermissions
            .Any(rp => rp.Permission.Name == permission && rp.IsGranted);

        // Check user-specific permissions (override role permissions)
        var hasUserPermission = user.UserPermissions
            .Where(up => !up.IsExpired())
            .Any(up => up.Permission.Name == permission && up.IsGranted);

        // User permissions override role permissions
        var userDenied = user.UserPermissions
            .Where(up => !up.IsExpired())
            .Any(up => up.Permission.Name == permission && !up.IsGranted);

        if (userDenied) return false;
        return hasUserPermission || hasRolePermission;
    }

    public async Task<List<string>> GetUserPermissionsAsync(int userId)
    {
        var user = await _userService.GetByIdAsync(userId);
        if (user == null) return new List<string>();

        var rolePermissions = user.Role.RolePermissions
            .Where(rp => rp.IsGranted)
            .Select(rp => rp.Permission.Name);

        var userPermissions = user.UserPermissions
            .Where(up => up.IsGranted && !up.IsExpired())
            .Select(up => up.Permission.Name);

        var deniedPermissions = user.UserPermissions
            .Where(up => !up.IsGranted && !up.IsExpired())
            .Select(up => up.Permission.Name);

        var allPermissions = rolePermissions.Union(userPermissions).Except(deniedPermissions);
        return allPermissions.ToList();
    }

    public async Task RefreshSessionAsync(string sessionToken, string? ipAddress = null)
    {
        var session = await _sessionRepository.GetBySessionTokenAsync(sessionToken);
        if (session != null && session.IsActive && !session.IsExpired())
        {
            session.ExpiresAt = DateTime.UtcNow.AddHours(24);
            session.UpdateLastAccess();
            if (!string.IsNullOrEmpty(ipAddress))
                session.IpAddress = ipAddress;
            
            await _sessionRepository.SaveAsync(session);
        }
    }
}
