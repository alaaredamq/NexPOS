using Application.IServices.Users;
using Core.Auditing;
using Core.Entities.Logging;
using Core.Entities.Users;
using Microsoft.Extensions.Localization;
using Web.Api.Account.Contracts;

namespace Web.Api.Account;

public interface IAccountApiService
{
    /// <summary>Null when the account no longer exists.</summary>
    Task<ProfileDto?> GetProfileAsync(int userId);

    /// <summary>Throws <see cref="AccountRuleException"/> for a wrong current PIN, a PIN that does not change, or a confirmation that differs. False when the account no longer exists.</summary>
    Task<bool> ChangePinAsync(int userId, ChangePinRequest request);
}

/// <summary>A field of the request is wrong; <see cref="Field"/> is the request property.</summary>
public sealed class AccountRuleException : Exception
{
    public string Field { get; }

    public AccountRuleException(string field, string message) : base(message) => Field = field;
}

/// <summary>The signed-in user's own profile and PIN. Same rules as the Razor <c>AccountController</c>.</summary>
public sealed class AccountApiService : IAccountApiService
{
    private readonly IUserService _users;
    private readonly IAuditTrail _audit;
    private readonly IStringLocalizer<SharedResource> _text;

    public AccountApiService(IUserService users, IAuditTrail audit, IStringLocalizer<SharedResource> text)
    {
        _users = users;
        _audit = audit;
        _text = text;
    }

    public async Task<ProfileDto?> GetProfileAsync(int userId)
    {
        var user = await FindAsync(userId);
        if (user is null) return null;

        var role = (await _users.GetRolesAsync()).FirstOrDefault(r => r.Id == user.RoleId);
        return new ProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = user.GetFullName(),
            RoleId = user.RoleId,
            RoleName = role?.Name ?? string.Empty,
            LastLoginAt = user.LastLoginAt,
            MustChangePin = user.MustChangePin,
        };
    }

    public async Task<bool> ChangePinAsync(int userId, ChangePinRequest request)
    {
        if (request.NewPin != request.ConfirmPin)
            throw new AccountRuleException("confirmPin", _text["PINs do not match"]);

        var user = await FindAsync(userId);
        if (user is null) return false;

        if (user.MustChangePin)
        {
            // An admin set this PIN and the user has just signed in with it: no need to type it again, but it has to change.
            if (await _users.ValidatePinAsync(user, request.NewPin))
                throw new AccountRuleException("newPin", _text["Please choose a PIN different from your current one"]);
        }
        else
        {
            if (string.IsNullOrEmpty(request.CurrentPin) || !await _users.ValidatePinAsync(user, request.CurrentPin))
            {
                // Someone at a signed-in screen guessing the PIN is worth knowing about.
                _audit.LogSecurity(AuditActions.PinChangeFailed, "PIN change refused: the current PIN was incorrect",
                    AuditOutcome.Failure, AuditLogSeverity.Warning, entityId: userId, entityLabel: user.Username);
                throw new AccountRuleException("currentPin", _text["Current PIN is incorrect"]);
            }

            if (request.NewPin == request.CurrentPin)
                throw new AccountRuleException("newPin", _text["New PIN must be different from your current PIN"]);
        }

        await _users.SetPinAsync(userId, request.NewPin, requireChangeOnNextLogin: false);
        return true;
    }

    // The repository throws ArgumentNullException (not null) for an unknown id.
    private async Task<User?> FindAsync(int id)
    {
        try
        {
            return await _users.GetByIdAsync(id);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }
}
