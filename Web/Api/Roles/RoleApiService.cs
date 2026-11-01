using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Roles.Contracts;
using Web.Services;

namespace Web.Api.Roles;

public interface IRoleApiService
{
    Task<PagedResult<RoleDto>> ListAsync(PagedQuery query);

    /// <summary>Null when the role does not exist.</summary>
    Task<RoleDetailsDto?> GetAsync(int id);

    /// <summary>Throws <see cref="RoleRuleException"/> for a missing, too long or taken name.</summary>
    Task<RoleDetailsDto> CreateAsync(RoleRequest request);

    /// <summary>Throws <see cref="RoleRuleException"/> for a locked (SuperAdmin) role, a bad name or an unknown role.</summary>
    Task<RoleDetailsDto> UpdateAsync(int id, RoleRequest request);

    /// <summary>Throws <see cref="RoleRuleException"/> for a built-in role, a role users still hold, or an unknown role.</summary>
    Task DeleteAsync(int id);
}

/// <summary>A role change the shared service refused. <see cref="Code"/> is one of <see cref="RoleErrors"/>; <see cref="Exception.Message"/> is already localized.</summary>
public sealed class RoleRuleException : Exception
{
    public string Code { get; }

    public RoleRuleException(string code, string message) : base(message) => Code = code;
}

/// <summary>Maps <see cref="IRoleAccessService"/> to the API's DTOs and its English refusals to localized messages. Cache invalidation (so open sessions see an edit on their next request) stays in that service.</summary>
public sealed class RoleApiService : IRoleApiService
{
    private static readonly string[] SortColumns = { "name", "userCount", "permissionCount", "createdAt" };

    private readonly IRoleAccessService _roles;
    private readonly IStringLocalizer<SharedResource> _text;

    public RoleApiService(IRoleAccessService roles, IStringLocalizer<SharedResource> text)
    {
        _roles = roles;
        _text = text;
    }

    public async Task<PagedResult<RoleDto>> ListAsync(PagedQuery query)
    {
        IEnumerable<RoleSummary> rows = await _roles.ListAsync();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || (r.Description ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return PagedResult<RoleDto>.From(Sort(rows.Select(ToDto), query), query);
    }

    public async Task<RoleDetailsDto?> GetAsync(int id)
        => await _roles.GetAsync(id) is { } role ? ToDetails(role) : null;

    public async Task<RoleDetailsDto> CreateAsync(RoleRequest request)
    {
        var result = await _roles.CreateAsync(request.Name, request.Description, request.Permissions);
        return await ReadAsync(result);
    }

    public async Task<RoleDetailsDto> UpdateAsync(int id, RoleRequest request)
    {
        var result = await _roles.UpdateAsync(id, request.Name, request.Description, request.Permissions);
        return await ReadAsync(result);
    }

    public async Task DeleteAsync(int id)
    {
        var result = await _roles.DeleteAsync(id);
        if (!result.Success) throw Refusal(result);
    }

    private async Task<RoleDetailsDto> ReadAsync(RoleResult result)
    {
        if (!result.Success) throw Refusal(result);
        return ToDetails((await _roles.GetAsync(result.RoleId!.Value))!);
    }

    private RoleRuleException Refusal(RoleResult result)
    {
        var code = result.Code ?? RoleErrors.NotFound;
        var message = code switch
        {
            RoleErrors.SuperAdminLocked => _text["Role_SuperAdminLocked"],
            RoleErrors.SystemRole => _text["Role_SystemRole"],
            RoleErrors.InUse => _text["Role_InUse"],
            RoleErrors.NameRequired => _text["Role_NameRequired"],
            RoleErrors.NameTooLong => _text["Role_NameTooLong"],
            RoleErrors.NameTaken => _text["Role_NameTaken"],
            _ => _text["Role_NotFound"],
        };
        return new RoleRuleException(code, message);
    }

    private static RoleDto ToDto(RoleSummary r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description,
        IsSystemRole = r.IsSystemRole,
        IsSuperAdmin = r.IsSuperAdmin,
        UserCount = r.UserCount,
        PermissionCount = r.Permissions.Length,
        CreatedAt = r.CreatedAt,
    };

    private static RoleDetailsDto ToDetails(RoleSummary r) => new() { Role = ToDto(r), Permissions = r.Permissions };

    private static IEnumerable<RoleDto> Sort(IEnumerable<RoleDto> rows, PagedQuery query)
    {
        var column = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "name";
        var desc = query.Descending;

        return column switch
        {
            "userCount" => Order(rows, r => r.UserCount, desc),
            "permissionCount" => Order(rows, r => r.PermissionCount, desc),
            "createdAt" => Order(rows, r => r.CreatedAt, desc),
            _ => Order(rows, r => r.Name, desc),
        };

        static IEnumerable<RoleDto> Order<TKey>(IEnumerable<RoleDto> source, Func<RoleDto, TKey> key, bool descending)
        {
            var ordered = descending
                ? source.OrderByDescending(key, Comparer<TKey>.Default)
                : source.OrderBy(key, Comparer<TKey>.Default);
            return ordered.ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Id);
        }
    }
}
