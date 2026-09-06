using Core.Entities.Users;

namespace Application.Models;

public class LoginResult
{
    public bool IsSuccess { get; set; }
    public string? Token { get; set; }
    public string? SessionToken { get; set; }
    public string? RefreshToken { get; set; }
    public User? User { get; set; }
    public string? ErrorMessage { get; set; }
    public bool MustChangePassword { get; set; }

    public static LoginResult Success(string token, string sessionToken, string refreshToken, User user)
    {
        return new LoginResult
        {
            IsSuccess = true,
            Token = token,
            SessionToken = sessionToken,
            RefreshToken = refreshToken,
            User = user,
            MustChangePassword = user.MustChangePassword
        };
    }

    public static LoginResult Failure(string errorMessage)
    {
        return new LoginResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
