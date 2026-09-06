using Application.IServices.Base;
using Core.Entities.Users;

namespace Application.IServices.Users;

public interface IUserService : IBaseService
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByEmailAsync(string email);
    Task<List<User>> GetAllAsync();
    Task<List<User>> GetByRoleIdAsync(int roleId);
    Task<User> GetByIdAsync(int id);
    Task<User> CreateAsync(User user, string password);
    Task UpdateAsync(int id, User user);
    Task UpdatePasswordAsync(int userId, string currentPassword, string newPassword);
    Task<bool> ValidatePasswordAsync(User user, string password);
    /// <summary>
    /// Sets (or resets) a user's PIN. <paramref name="requireChangeOnNextLogin"/> should be true
    /// when an admin is setting it on someone else's behalf, false when users set their own.
    /// </summary>
    Task SetPinAsync(int userId, string pin, bool requireChangeOnNextLogin = true);
    Task<bool> ValidatePinAsync(User user, string pin);
    Task<bool> IsUsernameUniqueAsync(string username, int? excludeUserId = null);
    Task<bool> IsEmailUniqueAsync(string email, int? excludeUserId = null);
    Task DeleteAsync(int id);

    Task<List<Role>> GetRolesAsync();
    Task<List<UserSession>> GetActiveSessionsAsync();
    /// <summary>
    /// Deactivates a single session by id. Returns false if no active session with that id exists.
    /// </summary>
    Task<bool> TerminateSessionAsync(int sessionId);
}
