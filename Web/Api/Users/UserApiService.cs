using Application.IServices.Users;
using Core.Entities.Users;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Users.Contracts;
using Web.Helpers;
using Web.Services;

namespace Web.Api.Users;

public interface IUserApiService
{
    Task<PagedResult<UserDto>> ListAsync(PagedQuery query);

    Task<UserSummaryDto> SummaryAsync();

    /// <summary>Null when the user does not exist.</summary>
    Task<UserDto?> GetAsync(int id);

    /// <summary>Throws <see cref="UserRuleException"/> for a taken username / email, an unknown role or a username that is empty once trimmed.</summary>
    Task<UserDto> CreateAsync(UserCreateRequest request);

    /// <summary>Null when the user does not exist. Throws <see cref="UserRuleException"/> for a taken email or an unknown role.</summary>
    Task<UserDto?> UpdateAsync(int id, UserUpdateRequest request);

    /// <summary>Throws <see cref="UserInUseException"/> for a user with sales, receipts or sessions on record; false when the user does not exist.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>The user must choose their own PIN at the next sign-in. False when the user does not exist.</summary>
    Task<bool> ResetPinAsync(int id, string newPin);

    /// <summary>Null when the user does not exist.</summary>
    Task<UserDto?> SetActiveAsync(int id, bool isActive);
}

/// <summary>A field of the request breaks a rule the DTO attributes cannot judge; <see cref="Field"/> is the request property.</summary>
public sealed class UserRuleException : Exception
{
    public string Field { get; }

    public UserRuleException(string field, string message) : base(message) => Field = field;
}

/// <summary>The user cannot be removed because sales, receipts or sessions point at them: deactivate instead.</summary>
public sealed class UserInUseException : Exception
{
    public UserInUseException(string message) : base(message)
    {
    }
}

/// <summary>Maps the shared user and role services to the API's DTOs. The rules (PIN hashing, access cache) stay in those services.</summary>
public sealed class UserApiService : IUserApiService
{
    private static readonly string[] SortColumns = { "name", "username", "email", "role", "isActive", "lastLoginAt", "createdAt" };

    private readonly IUserService _users;
    private readonly IRoleAccessService _roles;
    private readonly IStringLocalizer<SharedResource> _text;

    public UserApiService(IUserService users, IRoleAccessService roles, IStringLocalizer<SharedResource> text)
    {
        _users = users;
        _roles = roles;
        _text = text;
    }

    public async Task<PagedResult<UserDto>> ListAsync(PagedQuery query)
    {
        var roleNames = await RoleNamesAsync();
        IEnumerable<UserDto> rows = (await _users.GetAllAsync()).Select(u => ToDto(u, roleNames));

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(u =>
                u.Username.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                u.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                $"{u.FirstName} {u.LastName}".Contains(term, StringComparison.OrdinalIgnoreCase) ||
                u.RoleName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return PagedResult<UserDto>.From(Sort(rows, query), query);
    }

    public async Task<UserSummaryDto> SummaryAsync()
    {
        var users = await _users.GetAllAsync();
        var roles = await _roles.ListAsync();
        // "Admin" is whoever can manage users and roles - role names are the store's to choose.
        var adminRoleIds = roles.Where(r => r.Permissions.Contains(PermissionHelper.Permissions.Users_Manage)).Select(r => r.Id).ToHashSet();

        return new UserSummaryDto
        {
            TotalUsers = users.Count,
            TotalRoles = roles.Count,
            ActiveUsers = users.Count(u => u.IsActive),
            AdminUsers = users.Count(u => adminRoleIds.Contains(u.RoleId)),
        };
    }

    public async Task<UserDto?> GetAsync(int id)
    {
        var user = await FindAsync(id);
        return user is null ? null : ToDto(user, await RoleNamesAsync());
    }

    public async Task<UserDto> CreateAsync(UserCreateRequest request)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim();
        if (username.Length == 0) throw new UserRuleException("username", _text["Usr_UsernameInvalid"]);

        var roleNames = await RoleNamesAsync();
        if (!roleNames.ContainsKey(request.RoleId)) throw new UserRuleException("roleId", _text["Usr_UnknownRole"]);
        if (!await _users.IsUsernameUniqueAsync(username)) throw new UserRuleException("username", _text["Usr_UsernameTaken"]);
        if (!await _users.IsEmailUniqueAsync(email)) throw new UserRuleException("email", _text["Usr_EmailTaken"]);

        var user = new User(username, email, "temp_hash", request.RoleId, Clean(request.FirstName), Clean(request.LastName))
        {
            IsActive = request.IsActive,
        };

        // The admin-chosen PIN is the account's only credential; the user must change it at first sign-in.
        var created = await _users.CreateWithPinAsync(user, request.Pin);
        _roles.InvalidateAccess();
        return ToDto(created, roleNames);
    }

    public async Task<UserDto?> UpdateAsync(int id, UserUpdateRequest request)
    {
        var user = await FindAsync(id);
        if (user is null) return null;

        var email = request.Email.Trim();
        var roleNames = await RoleNamesAsync();
        if (!await _users.IsEmailUniqueAsync(email, id)) throw new UserRuleException("email", _text["Usr_EmailTaken"]);
        if (!roleNames.ContainsKey(request.RoleId)) throw new UserRuleException("roleId", _text["Usr_UnknownRole"]);

        user.FirstName = Clean(request.FirstName);
        user.LastName = Clean(request.LastName);
        user.Email = email;
        user.IsActive = request.IsActive;
        user.RoleId = request.RoleId;

        await _users.UpdateAsync(id, user);
        _roles.InvalidateAccess(); // role and active flag feed the signed-in user's permissions
        return ToDto(user, roleNames);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (await FindAsync(id) is null) return false;

        try
        {
            await _users.DeleteAsync(id);
        }
        catch (InvalidOperationException)
        {
            // Sales, receipts, sessions or backups made by this user still reference them.
            throw new UserInUseException(_text["Usr_InUse"]);
        }

        _roles.InvalidateAccess();
        return true;
    }

    public async Task<bool> ResetPinAsync(int id, string newPin)
    {
        if (await FindAsync(id) is null) return false;

        // An admin-set PIN must be changed by the user on their next sign-in.
        await _users.SetPinAsync(id, newPin, requireChangeOnNextLogin: true);
        return true;
    }

    public async Task<UserDto?> SetActiveAsync(int id, bool isActive)
    {
        var user = await FindAsync(id);
        if (user is null) return null;

        user.IsActive = isActive;
        await _users.UpdateAsync(id, user);
        _roles.InvalidateAccess();
        return ToDto(user, await RoleNamesAsync());
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

    private async Task<Dictionary<int, string>> RoleNamesAsync()
        => (await _users.GetRolesAsync()).ToDictionary(r => r.Id, r => r.Name);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static UserDto ToDto(User u, IReadOnlyDictionary<int, string> roleNames) => new()
    {
        Id = u.Id,
        Username = u.Username,
        Email = u.Email,
        FirstName = u.FirstName,
        LastName = u.LastName,
        FullName = u.GetFullName(),
        RoleId = u.RoleId,
        RoleName = roleNames.GetValueOrDefault(u.RoleId, string.Empty),
        IsActive = u.IsActive,
        MustChangePin = u.MustChangePin,
        LastLoginAt = u.LastLoginAt,
        CreatedAt = u.CreatedAt,
    };

    private static IEnumerable<UserDto> Sort(IEnumerable<UserDto> rows, PagedQuery query)
    {
        var column = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "name";
        var desc = query.Descending;

        return column switch
        {
            "username" => Order(rows, u => u.Username, desc),
            "email" => Order(rows, u => u.Email, desc),
            "role" => Order(rows, u => u.RoleName, desc),
            "isActive" => Order(rows, u => u.IsActive, desc),
            "lastLoginAt" => Order(rows, u => u.LastLoginAt ?? DateTime.MinValue, desc),
            "createdAt" => Order(rows, u => u.CreatedAt, desc),
            _ => Order(rows, u => u.FullName, desc),
        };

        static IEnumerable<UserDto> Order<TKey>(IEnumerable<UserDto> source, Func<UserDto, TKey> key, bool descending)
        {
            // Name, then id, is the tie-breaker, so equal rows never swap between pages.
            var ordered = descending
                ? source.OrderByDescending(key, Comparer<TKey>.Default)
                : source.OrderBy(key, Comparer<TKey>.Default);
            return ordered.ThenBy(u => u.FullName, StringComparer.CurrentCultureIgnoreCase).ThenBy(u => u.Id);
        }
    }
}
