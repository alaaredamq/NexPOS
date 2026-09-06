using Core.Entities.Users;

namespace Application.IServices.Auth;

public interface IJwtService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    string GenerateSessionToken();
    bool ValidateToken(string token);
    Task<User?> GetUserFromTokenAsync(string token);
}
