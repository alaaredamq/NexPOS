using System.Collections.Concurrent;
using Core.Entities.Configuration;
using Core.Entities.Users;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Web.Helpers;

namespace Web.Services;

/// <summary>What a signed-in user may do right now: their current role and that role's grants.</summary>
public sealed record UserAccess(int RoleId, string RoleName, string[] Permissions);

/// <summary>A role as the Users page shows it. <see cref="UserCount"/> drives the "in use" warning on delete.</summary>
public sealed record RoleSummary(
    int Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsSuperAdmin,
    int UserCount,
    string[] Permissions,
    DateTime CreatedAt);

public sealed record RoleResult(bool Success, string? Error = null, int? RoleId = null, string? Code = null)
{
    public static RoleResult Ok(int roleId) => new(true, null, roleId);
    /// <param name="error">The English sentence the Razor page shows.</param>
    /// <param name="code">Stable machine string (<see cref="RoleErrors"/>) for the JSON API; the Razor page only shows <paramref name="error"/>.</param>
    public static RoleResult Fail(string error, string? code = null) => new(false, error, null, code);
}

/// <summary>
/// Per-user access snapshots shared by every request. Entries expire on their own, and any change to a role or
/// to who holds it clears the lot - so an edit on the Users page reaches people who are already signed in
/// on their next request instead of at their next sign-in.
/// </summary>
public sealed class AccessCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<int, (UserAccess? Access, DateTime LoadedUtc)> _entries = new();

    public bool TryGet(int userId, out UserAccess? access)
    {
        if (_entries.TryGetValue(userId, out var entry) && DateTime.UtcNow - entry.LoadedUtc < MaxAge)
        {
            access = entry.Access;
            return true;
        }

        access = null;
        return false;
    }

    public void Set(int userId, UserAccess? access) => _entries[userId] = (access, DateTime.UtcNow);

    public void Clear() => _entries.Clear();
}

/// <summary>Stable codes of a refused role change (<see cref="RoleResult.Code"/>); the JSON API turns them into localized messages.</summary>
public static class RoleErrors
{
    public const string NotFound = "role_not_found";
    public const string SuperAdminLocked = "role_super_admin_locked";
    public const string SystemRole = "role_system";
    public const string InUse = "role_in_use";
    public const string NameRequired = "role_name_required";
    public const string NameTooLong = "role_name_too_long";
    public const string NameTaken = "role_name_taken";
}

public interface IRoleAccessService
{
    /// <summary>Startup: makes sure every code-defined permission has a row, and seeds the default grants once.</summary>
    Task EnsureCatalogAsync();

    /// <summary>Null when the user no longer exists or is disabled.</summary>
    Task<UserAccess?> GetUserAccessAsync(int userId);

    Task<List<RoleSummary>> ListAsync();
    Task<RoleSummary?> GetAsync(int roleId);
    Task<RoleResult> CreateAsync(string? name, string? description, IEnumerable<string>? permissions);
    Task<RoleResult> UpdateAsync(int roleId, string? name, string? description, IEnumerable<string>? permissions);
    Task<RoleResult> DeleteAsync(int roleId);

    /// <summary>Call after changing a user's role or active flag so open sessions pick it up.</summary>
    void InvalidateAccess();
}

public class RoleAccessService : IRoleAccessService
{
    private const string DefaultsSeededMarker = "RoleDefaultsSeeded";
    private const int MaxNameLength = 100;

    private readonly ErpDbContext _db;
    private readonly AccessCache _cache;

    public RoleAccessService(ErpDbContext db, AccessCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private static bool IsSuperAdmin(string roleName, bool isSystemRole)
        => isSystemRole && string.Equals(roleName, PermissionHelper.Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

    public void InvalidateAccess() => _cache.Clear();

    public async Task EnsureCatalogAsync()
    {
        var catalog = PermissionHelper.Permissions.All;
        var permissions = await _db.Permissions.Where(p => catalog.Contains(p.Name)).ToDictionaryAsync(p => p.Name);

        foreach (var name in catalog.Where(n => !permissions.ContainsKey(n)))
        {
            var dot = name.IndexOf('.');
            var category = dot > 0 ? name[..dot] : name;
            var action = dot > 0 ? name[(dot + 1)..] : "Access";
            var permission = new Permission(name, category, action, $"{category}: {action}");
            _db.Permissions.Add(permission);
            permissions[name] = permission;
        }
        await _db.SaveChangesAsync();

        // The built-in roles used to get their grants from a dictionary in code. Copy that into the database
        // exactly once; after that the Users page owns them (re-seeding would undo every edit on restart).
        if (await _db.AppSettings.AnyAsync(s => s.Name == DefaultsSeededMarker))
            return;

        var defaultRoleNames = PermissionHelper.DefaultRolePermissions.Keys.ToArray();
        var roles = await _db.Roles.Where(r => defaultRoleNames.Contains(r.Name)).ToListAsync();
        var roleIds = roles.Select(r => r.Id).ToList();
        var existing = (await _db.RolePermissions
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Select(rp => new { rp.RoleId, rp.PermissionId })
                .ToListAsync())
            .Select(x => (x.RoleId, x.PermissionId))
            .ToHashSet();

        foreach (var role in roles)
        {
            foreach (var name in PermissionHelper.Permissions.Normalize(PermissionHelper.DefaultRolePermissions[role.Name]))
            {
                if (existing.Add((role.Id, permissions[name].Id)))
                    _db.RolePermissions.Add(new RolePermission(role.Id, permissions[name].Id));
            }
        }

        _db.AppSettings.Add(new AppSetting(DefaultsSeededMarker, "true",
            "Built-in roles have been given their default permissions; the Users page manages them from here on.", "bool"));
        await _db.SaveChangesAsync();
    }

    public async Task<UserAccess?> GetUserAccessAsync(int userId)
    {
        if (_cache.TryGet(userId, out var cached))
            return cached;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive && !u.IsDeleted)
            .Select(u => new { u.RoleId, RoleName = u.Role.Name, u.Role.IsSystemRole })
            .FirstOrDefaultAsync();

        UserAccess? access = null;
        if (user is not null)
        {
            string[] permissions;
            if (IsSuperAdmin(user.RoleName, user.IsSystemRole))
            {
                permissions = PermissionHelper.Permissions.All;
            }
            else
            {
                var granted = await _db.RolePermissions.AsNoTracking()
                    .Where(rp => rp.RoleId == user.RoleId && rp.IsGranted)
                    .Select(rp => rp.Permission.Name)
                    .ToListAsync();
                permissions = PermissionHelper.Permissions.Normalize(granted).ToArray();
            }

            access = new UserAccess(user.RoleId, user.RoleName, permissions);
        }

        _cache.Set(userId, access);
        return access;
    }

    public async Task<List<RoleSummary>> ListAsync()
    {
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();

        var counts = await _db.Users.AsNoTracking()
            .GroupBy(u => u.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);

        var grants = (await _db.RolePermissions.AsNoTracking()
                .Where(rp => rp.IsGranted)
                .Select(rp => new { rp.RoleId, rp.Permission.Name })
                .ToListAsync())
            .GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => PermissionHelper.Permissions.Normalize(g.Select(x => x.Name)).ToArray());

        return roles.Select(r =>
        {
            var superAdmin = IsSuperAdmin(r.Name, r.IsSystemRole);
            return new RoleSummary(
                r.Id, r.Name, r.Description, r.IsSystemRole, superAdmin,
                counts.GetValueOrDefault(r.Id),
                // Reported as the whole catalog so the editor shows SuperAdmin ticked through.
                superAdmin ? PermissionHelper.Permissions.All : grants.GetValueOrDefault(r.Id) ?? Array.Empty<string>(),
                r.CreatedAt);
        }).ToList();
    }

    public async Task<RoleSummary?> GetAsync(int roleId)
        => (await ListAsync()).FirstOrDefault(r => r.Id == roleId);

    public async Task<RoleResult> CreateAsync(string? name, string? description, IEnumerable<string>? permissions)
    {
        name = name?.Trim() ?? string.Empty;
        var refused = await ValidateNameAsync(name, exceptRoleId: null);
        if (refused is not null) return refused;

        var role = new Role(name, string.IsNullOrWhiteSpace(description) ? null : description.Trim());
        var wanted = PermissionHelper.Permissions.Normalize(permissions);
        var permissionIds = await PermissionIdsAsync(wanted);
        foreach (var permissionName in wanted)
            role.RolePermissions.Add(new RolePermission(0, permissionIds[permissionName]));

        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        _cache.Clear();
        return RoleResult.Ok(role.Id);
    }

    public async Task<RoleResult> UpdateAsync(int roleId, string? name, string? description, IEnumerable<string>? permissions)
    {
        var role = await _db.Roles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Id == roleId);
        if (role is null) return RoleResult.Fail("Role not found.", RoleErrors.NotFound);
        if (IsSuperAdmin(role.Name, role.IsSystemRole))
            return RoleResult.Fail("The SuperAdmin role always holds every permission and cannot be edited.", RoleErrors.SuperAdminLocked);

        name = name?.Trim() ?? string.Empty;
        var refused = await ValidateNameAsync(name, exceptRoleId: roleId);
        if (refused is not null) return refused;

        role.Name = name;
        role.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        // The editor always submits the full tick state, so the stored grants are made to match it exactly.
        // Rows for permissions that are no longer in the catalog are not the editor's to touch.
        var wanted = PermissionHelper.Permissions.Normalize(permissions).ToHashSet();
        var have = new HashSet<string>();
        foreach (var grant in role.RolePermissions.ToList())
        {
            var permissionName = grant.Permission.Name;
            if (!PermissionHelper.Permissions.IsValid(permissionName)) continue;

            if (wanted.Contains(permissionName))
            {
                grant.IsGranted = true;
                have.Add(permissionName);
            }
            else
            {
                _db.RolePermissions.Remove(grant);
            }
        }

        var missing = wanted.Where(w => !have.Contains(w)).ToList();
        var permissionIds = await PermissionIdsAsync(missing);
        foreach (var permissionName in missing)
            role.RolePermissions.Add(new RolePermission(role.Id, permissionIds[permissionName]));

        await _db.SaveChangesAsync();
        _cache.Clear();
        return RoleResult.Ok(role.Id);
    }

    public async Task<RoleResult> DeleteAsync(int roleId)
    {
        var role = await _db.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Id == roleId);
        if (role is null) return RoleResult.Fail("Role not found.", RoleErrors.NotFound);
        if (role.IsSystemRole) return RoleResult.Fail("Built-in roles cannot be deleted.", RoleErrors.SystemRole);

        // The foreign key is restrictive, so this would fail at the database anyway - answer with something
        // the page can show instead of a constraint violation.
        if (await _db.Users.AnyAsync(u => u.RoleId == roleId))
            return RoleResult.Fail("Users still hold this role - move them to another role first.", RoleErrors.InUse);

        _db.RolePermissions.RemoveRange(role.RolePermissions);
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
        _cache.Clear();
        return RoleResult.Ok(roleId);
    }

    private async Task<RoleResult?> ValidateNameAsync(string name, int? exceptRoleId)
    {
        if (name.Length == 0) return RoleResult.Fail("Name is required.", RoleErrors.NameRequired);
        if (name.Length > MaxNameLength) return RoleResult.Fail($"Name can be at most {MaxNameLength} characters.", RoleErrors.NameTooLong);

        var lowered = name.ToLower();
        if (await _db.Roles.AnyAsync(r => r.Id != exceptRoleId && r.Name.ToLower() == lowered))
            return RoleResult.Fail("A role with that name already exists.", RoleErrors.NameTaken);

        return null;
    }

    private async Task<Dictionary<string, int>> PermissionIdsAsync(IReadOnlyCollection<string> names)
    {
        if (names.Count == 0) return new Dictionary<string, int>();
        return await _db.Permissions.Where(p => names.Contains(p.Name)).ToDictionaryAsync(p => p.Name, p => p.Id);
    }
}
