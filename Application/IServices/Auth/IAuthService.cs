using Application.Models;
using Core.Entities.Users;

namespace Application.IServices.Auth;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(string username, string password, string? ipAddress = null, string? userAgent = null);
    Task<LoginResult> LoginWithPinAsync(string username, string pin, string? ipAddress = null, string? userAgent = null);
    Task LogoutAsync(string sessionToken);
    Task<UserSession?> ValidateSessionAsync(string sessionToken);
    Task<User?> GetCurrentUserAsync(string sessionToken);
    Task<bool> HasPermissionAsync(int userId, string permission);
    Task<List<string>> GetUserPermissionsAsync(int userId);
    Task RefreshSessionAsync(string sessionToken, string? ipAddress = null);
}
