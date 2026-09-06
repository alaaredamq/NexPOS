using Application.IServices.Auth;
using Application.IServices.Users;
using Application.Models;
using Core.Entities.Users;
using Core.IRepositories.Users;

namespace Application.Services.Auth;

public class AuthService : IAuthService
{
    private readonly IUserService _userService;
    private readonly IJwtService _jwtService;
    private readonly IUserSessionRepository _sessionRepository;

    public AuthService(IUserService userService, IJwtService jwtService, IUserSessionRepository sessionRepository)
    {
        _userService = userService;
        _jwtService = jwtService;
        _sessionRepository = sessionRepository;
    }

    public async Task<LoginResult> LoginAsync(string username, string password, string? ipAddress = null, string? userAgent = null)
    {
        var user = await _userService.GetByUsernameAsync(username);
        if (user == null)
            return LoginResult.Failure("Invalid username or password");

        if (!user.IsActive)
            return LoginResult.Failure("Account is disabled");

        if (!await _userService.ValidatePasswordAsync(user, password))
            return LoginResult.Failure("Invalid username or password");

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

        return LoginResult.Success(accessToken, sessionToken, refreshToken, user);
    }

    public async Task<LoginResult> LoginWithPinAsync(string username, string pin, string? ipAddress = null, string? userAgent = null)
    {
        var user = await _userService.GetByUsernameAsync(username);
        if (user == null)
            return LoginResult.Failure("Invalid username or PIN");

        if (!user.IsActive)
            return LoginResult.Failure("Account is disabled");

        if (!await _userService.ValidatePinAsync(user, pin))
            return LoginResult.Failure("Invalid username or PIN");

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

        return LoginResult.Success(accessToken, sessionToken, refreshToken, user);
    }

    public async Task LogoutAsync(string sessionToken)
    {
        var session = await _sessionRepository.GetBySessionTokenAsync(sessionToken);
        if (session != null)
        {
            session.Deactivate();
            await _sessionRepository.SaveAsync(session);
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
