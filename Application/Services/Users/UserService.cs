using System.Security.Cryptography;
using Application.IServices.Users;
using Core.Auditing;
using Core.Entities.Users;
using Core.IRepositories.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Users;

public class UserService : IUserService
{
    private readonly IUserRepository _repo;
    private readonly IRoleRepository _roleRepo;
    private readonly IUserSessionRepository _sessionRepo;
    private readonly IAuditTrail _auditTrail;

    public UserService(IUserRepository repo, IRoleRepository roleRepo, IUserSessionRepository sessionRepo, IAuditTrail auditTrail)
    {
        _repo = repo;
        _roleRepo = roleRepo;
        _sessionRepo = sessionRepo;
        _auditTrail = auditTrail;
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

    public async Task<User> CreateWithPinAsync(User user, string pin)
    {
        // Sign-in is PIN-only. PasswordHash is still a required column (and the legacy password
        // endpoint reads it), so new accounts get a random password nobody knows rather than a usable one.
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        user.PinHash = BCrypt.Net.BCrypt.HashPassword(pin);
        user.MustChangePin = true;
        var created = await _repo.SaveAsync(user);

        _auditTrail.LogSecurity(AuditActions.PinChanged,
            $"Initial PIN set for '{created.Username}' (must be changed at next sign-in)",
            entityId: created.Id, entityLabel: created.Username,
            details: new { mustChangeAtNextSignIn = true });
        return created;
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

        // Who did it is the row's user; this says whose password. (The value itself is never recorded.)
        _auditTrail.LogSecurity(AuditActions.PasswordChanged, $"Password changed for '{user.Username}'",
            entityId: user.Id, entityLabel: user.Username);
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

        // The row's user is who made the change; this names whose PIN it is - so "admin reset cashier1's PIN"
        // and "cashier1 changed their own PIN" are told apart. (The PIN and its hash are never recorded.)
        _auditTrail.LogSecurity(AuditActions.PinChanged,
            requireChangeOnNextLogin
                ? $"PIN reset for '{user.Username}' (must be changed at next sign-in)"
                : $"PIN changed for '{user.Username}'",
            entityId: user.Id, entityLabel: user.Username,
            details: new { mustChangeAtNextSignIn = requireChangeOnNextLogin });
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
        try
        {
            await _repo.DeleteAsync(existing);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // Sales, receipts, sessions or backups made by this user still reference them.
            throw new InvalidOperationException(
                "This user has activity on record (sales, receipts or sessions), so they can't be removed. Deactivate them instead.", ex);
        }
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
