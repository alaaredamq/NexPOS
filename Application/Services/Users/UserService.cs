using Application.IServices.Users;
using Core.Entities.Users;
using Core.IRepositories.Users;

namespace Application.Services.Users;

public class UserService : IUserService
{
    private readonly IUserRepository _repo;
    private readonly IRoleRepository _roleRepo;
    private readonly IUserSessionRepository _sessionRepo;

    public UserService(IUserRepository repo, IRoleRepository roleRepo, IUserSessionRepository sessionRepo)
    {
        _repo = repo;
        _roleRepo = roleRepo;
        _sessionRepo = sessionRepo;
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _repo.GetByUsernameAsync(username);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _repo.GetByEmailAsync(email);
    }

    public async Task<List<User>> GetAllAsync()
    {
        return await _repo.GetAllAsync();
    }

    public async Task<List<User>> GetByRoleIdAsync(int roleId)
    {
        return await _repo.GetByRoleIdAsync(roleId);
    }

    public async Task<User> GetByIdAsync(int id)
    {
        return await _repo.GetByIdAsync(id);
    }

    public async Task<User> CreateAsync(User user, string password)
    {
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        return await _repo.SaveAsync(user);
    }

    public async Task UpdateAsync(int id, User user)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.Username = user.Username;
        existing.Email = user.Email;
        existing.FirstName = user.FirstName;
        existing.LastName = user.LastName;
        existing.IsActive = user.IsActive;
        existing.RoleId = user.RoleId;
        existing.LastLoginAt = user.LastLoginAt;
        await _repo.SaveAsync(existing);
    }

    public async Task UpdatePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _repo.GetByIdAsync(userId);
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect");

        user.ChangePassword(BCrypt.Net.BCrypt.HashPassword(newPassword));
        await _repo.SaveAsync(user);
    }

    public async Task<bool> ValidatePasswordAsync(User user, string password)
    {
        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
    }

    public async Task SetPinAsync(int userId, string pin, bool requireChangeOnNextLogin = true)
    {
        var user = await _repo.GetByIdAsync(userId);
        user.PinHash = BCrypt.Net.BCrypt.HashPassword(pin);
        user.MustChangePin = requireChangeOnNextLogin;
        await _repo.SaveAsync(user);
    }

    public async Task<bool> ValidatePinAsync(User user, string pin)
    {
        return user.PinHash != null && BCrypt.Net.BCrypt.Verify(pin, user.PinHash);
    }

    public async Task<bool> IsUsernameUniqueAsync(string username, int? excludeUserId = null)
    {
        return await _repo.IsUsernameUniqueAsync(username, excludeUserId);
    }

    public async Task<bool> IsEmailUniqueAsync(string email, int? excludeUserId = null)
    {
        return await _repo.IsEmailUniqueAsync(email, excludeUserId);
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        await _repo.DeleteAsync(existing);
    }

    public async Task<List<Role>> GetRolesAsync()
    {
        return await _roleRepo.GetAllAsync();
    }

    public async Task<List<UserSession>> GetActiveSessionsAsync()
    {
        return await _sessionRepo.GetActiveSessionsAsync();
    }

    public async Task<bool> TerminateSessionAsync(int sessionId)
    {
        var session = await _sessionRepo.FindAsync(sessionId);
        if (session is not { IsActive: true })
            return false;

        session.Deactivate();
        await _sessionRepo.SaveAsync(session);
        return true;
    }
}
