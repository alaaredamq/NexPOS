using Core.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Web.Attributes;
using Web.Helpers;
using Application.IServices.Users;
using Core.Entities.Users;
using Web.Services;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Users_View)]
public class UsersController : BaseController
{
    private readonly IUserService _userService;
    private readonly IRoleAccessService _roleAccess;

    public UsersController(IUserService userService, IRoleAccessService roleAccess)
    {
        _userService = userService;
        _roleAccess = roleAccess;
    }

    private const string PinRuleMessage = "PIN must be exactly 4 digits";

    private static bool IsValidPin(string? pin) =>
        pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "User Management";
        ViewData["Subtitle"] = "Manage system users, roles, and permissions";

        var roles = await _userService.GetRolesAsync();
        ViewBag.Roles = roles.OrderBy(r => r.Name).ToList();
        ViewBag.RoleSummaries = await _roleAccess.ListAsync();
        ViewBag.Stats = await BuildStatsAsync();
        return View();
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Users_Create)]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        try
        {
            // Login is PIN-only and its keypad takes exactly 4 digits, so anything else could never sign in.
            if (string.IsNullOrWhiteSpace(dto.Pin) || !IsValidPin(dto.Pin))
            {
                return Json(new { success = false, message = PinRuleMessage });
            }

            var roleId = await ResolveRoleIdAsync(dto.Role);
            if (roleId == null)
            {
                return Json(new { success = false, message = $"Unknown role '{dto.Role}'" });
            }

            if (!await _userService.IsUsernameUniqueAsync(dto.Username))
            {
                return Json(new { success = false, message = "Username is already taken" });
            }

            if (!await _userService.IsEmailUniqueAsync(dto.Email))
            {
                return Json(new { success = false, message = "Email is already in use" });
            }

            var user = new User(dto.Username, dto.Email, "temp_hash", roleId.Value, dto.FirstName, dto.LastName)
            {
                IsActive = dto.IsActive
            };

            // The admin-chosen PIN is the account's only credential; the user must change it at first sign-in.
            await _userService.CreateWithPinAsync(user, dto.Pin);

            return Json(new { success = true, message = "User created successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Users_Manage)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleDto dto)
    {
        try
        {
            var result = await _roleAccess.CreateAsync(dto.Name, dto.Description, dto.Permissions);
            return Json(new { success = result.Success, message = result.Error ?? "Role created successfully", id = result.RoleId });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Users_Manage)]
    public async Task<IActionResult> GetRole(int id)
    {
        var role = await _roleAccess.GetAsync(id);
        if (role is null)
            return Json(new { success = false, message = "Role not found" });

        return Json(new
        {
            success = true,
            role = new
            {
                id = role.Id,
                name = role.Name,
                description = role.Description,
                isSuperAdmin = role.IsSuperAdmin,
                isSystemRole = role.IsSystemRole,
                userCount = role.UserCount,
                permissions = role.Permissions
            }
        });
    }

    [HttpPut]
    [RequirePermission(PermissionHelper.Permissions.Users_Manage)]
    public async Task<IActionResult> UpdateRole(int id, [FromBody] CreateRoleDto dto)
    {
        try
        {
            var result = await _roleAccess.UpdateAsync(id, dto.Name, dto.Description, dto.Permissions);
            return Json(new { success = result.Success, message = result.Error ?? "Role updated successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete]
    [RequirePermission(PermissionHelper.Permissions.Users_Manage)]
    public async Task<IActionResult> DeleteRole(int id)
    {
        try
        {
            var result = await _roleAccess.DeleteAsync(id);
            return Json(new { success = result.Success, message = result.Error ?? "Role deleted successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            return Json(await BuildStatsAsync());
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers(int start = 0, int length = 10, string searchValue = "", string sortColumn = "FirstName", string sortDirection = "asc")
    {
        try
        {
            var users = await _userService.GetAllAsync();
            var roleNames = await GetRoleNameMapAsync();

            if (!string.IsNullOrEmpty(searchValue))
            {
                users = users.Where(u =>
                    u.Username.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (u.FirstName + " " + u.LastName).Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            var pagedUsers = users.Skip(start).Take(length);

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = users.Count,
                recordsFiltered = users.Count,
                data = pagedUsers.Select(u => new
                {
                    id = u.Id,
                    userDisplay = $@"
                        <div class='d-flex align-items-center'>
                            <div class='user-avatar me-2'><i class='fas fa-user'></i></div>
                            <div>
                                <div class='user-name'>{Enc($"{u.FirstName} {u.LastName}".Trim())}</div>
                                <small class='text-muted'>{Enc(u.Username)}</small>
                            </div>
                        </div>",
                    email = u.Email,
                    roleBadge = GetRoleBadge(roleNames.GetValueOrDefault(u.RoleId, "Unknown")),
                    statusBadge = u.IsActive ? "<span class='badge bg-success'>Active</span>" : "<span class='badge bg-secondary'>Inactive</span>",
                    lastLogin = u.LastLoginAt.UtcToLocal()?.ToString("yyyy-MM-dd HH:mm") ?? "Never",
                    created = u.CreatedAt.UtcToLocal().ToString("yyyy-MM-dd"),
                    actions = GetUserActionButtons(u.Id, u.IsActive)
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetUser(int id)
    {
        try
        {
            var user = await _userService.GetByIdAsync(id);
            return Json(new
            {
                success = true,
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    roleId = user.RoleId,
                    isActive = user.IsActive
                }
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetRoles(int start = 0, int length = 10, string searchValue = "", string sortColumn = "Name", string sortDirection = "asc")
    {
        try
        {
            var roles = await _roleAccess.ListAsync();

            if (!string.IsNullOrEmpty(searchValue))
            {
                roles = roles.Where(r =>
                    r.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (r.Description ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            var recordsTotal = roles.Count;
            var ordered = (sortDirection?.ToLower() == "desc"
                    ? roles.OrderByDescending(r => r.Name)
                    : roles.OrderBy(r => r.Name))
                .ToList();
            var pagedRoles = ordered.Skip(start).Take(length);

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal,
                recordsFiltered = recordsTotal,
                data = pagedRoles.Select(r => new
                {
                    id = r.Id,
                    roleDisplay = $@"
                        <div class='d-flex align-items-center'>
                            <span class='badge {GetRoleBadgeClass(r.Name)} me-2'>{Enc(r.Name)}</span>
                        </div>",
                    description = Enc(r.Description ?? ""),
                    userCount = r.UserCount,
                    permissionCount = r.Permissions.Length,
                    created = r.CreatedAt.UtcToLocal().ToString("yyyy-MM-dd"),
                    actions = GetRoleActionButtons(r)
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetSessions(int start = 0, int length = 10, string searchValue = "", string sortColumn = "LoginTime", string sortDirection = "desc")
    {
        try
        {
            var sessions = await _userService.GetActiveSessionsAsync();

            if (!string.IsNullOrEmpty(searchValue))
            {
                sessions = sessions.Where(s =>
                    (s.User?.Username ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (s.User?.Email ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (s.IpAddress ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            var recordsTotal = sessions.Count;
            var pagedSessions = sessions.Skip(start).Take(length);

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal,
                recordsFiltered = recordsTotal,
                data = pagedSessions.Select(s => new
                {
                    id = s.Id,
                    userDisplay = $@"
                        <div class='d-flex align-items-center'>
                            <div class='user-avatar me-2'><i class='fas fa-user'></i></div>
                            <div>
                                <div class='user-name'>{Enc(s.User?.GetFullName() ?? "Unknown")}</div>
                                <small class='text-muted'>{Enc(s.User?.Email ?? "")}</small>
                            </div>
                        </div>",
                    deviceInfo = $"<i class='fas fa-desktop me-1'></i>{Enc(DescribeUserAgent(s.UserAgent))}",
                    ipAddress = Enc(s.IpAddress ?? "—"),
                    location = "—",
                    loginTime = s.CreatedAt.UtcToLocal().ToString("yyyy-MM-dd HH:mm"),
                    lastActivity = (s.LastAccessedAt ?? s.CreatedAt).UtcToLocal().ToString("yyyy-MM-dd HH:mm"),
                    actions = $"<button class='btn btn-sm btn-outline-danger' onclick='terminateSession({s.Id})' title='Terminate'><i class='fas fa-times'></i></button>"
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpPut]
    [RequirePermission(PermissionHelper.Permissions.Users_Edit)]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
    {
        try
        {
            var user = await _userService.GetByIdAsync(id);

            if (!await _userService.IsEmailUniqueAsync(dto.Email, id))
            {
                return Json(new { success = false, message = "Email is already in use" });
            }

            var roleNames = await GetRoleNameMapAsync();
            if (!roleNames.ContainsKey(dto.RoleId))
            {
                return Json(new { success = false, message = "Unknown role" });
            }

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.Email = dto.Email;
            user.IsActive = dto.IsActive;
            user.RoleId = dto.RoleId;

            await _userService.UpdateAsync(id, user);
            _roleAccess.InvalidateAccess(); // role and active flag feed the signed-in user's permissions
            return Json(new { success = true, message = "User updated successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete]
    [RequirePermission(PermissionHelper.Permissions.Users_Delete)]
    public async Task<IActionResult> DeleteUser(int id)
    {
        try
        {
            await _userService.DeleteAsync(id);
            _roleAccess.InvalidateAccess();
            return Json(new { success = true, message = "User deleted successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Users_Edit)]
    public async Task<IActionResult> ResetPin(int id, [FromBody] ResetPinDto dto)
    {
        try
        {
            if (!IsValidPin(dto.NewPin))
            {
                return Json(new { success = false, message = PinRuleMessage });
            }

            // Admin-set PIN must be changed by the user on their next login
            await _userService.SetPinAsync(id, dto.NewPin, requireChangeOnNextLogin: true);
            return Json(new { success = true, message = "PIN reset successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Users_Edit)]
    public async Task<IActionResult> ToggleUserStatus(int id)
    {
        try
        {
            var user = await _userService.GetByIdAsync(id);
            user.IsActive = !user.IsActive;
            await _userService.UpdateAsync(id, user);
            _roleAccess.InvalidateAccess();
            return Json(new { success = true, message = "User status updated successfully", isActive = user.IsActive });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Users_Manage)]
    public async Task<IActionResult> TerminateSession(int id)
    {
        try
        {
            var terminated = await _userService.TerminateSessionAsync(id);
            return terminated
                ? Json(new { success = true, message = "Session terminated successfully" })
                : Json(new { success = false, message = "Session not found or already ended" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    #region Helpers

    private async Task<UserStatsDto> BuildStatsAsync()
    {
        var users = await _userService.GetAllAsync();
        var roles = await _roleAccess.ListAsync();
        // "Admin" is whoever can manage users and roles - role names are the store's to choose.
        var adminRoleIds = roles
            .Where(r => r.Permissions.Contains(PermissionHelper.Permissions.Users_Manage))
            .Select(r => r.Id)
            .ToHashSet();

        return new UserStatsDto(
            TotalUsers: users.Count,
            TotalRoles: roles.Count,
            ActiveUsers: users.Count(u => u.IsActive),
            AdminUsers: users.Count(u => adminRoleIds.Contains(u.RoleId)));
    }

    private async Task<Dictionary<int, string>> GetRoleNameMapAsync()
    {
        var roles = await _userService.GetRolesAsync();
        return roles.ToDictionary(r => r.Id, r => r.Name);
    }

    private async Task<int?> ResolveRoleIdAsync(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return null;

        var roles = await _userService.GetRolesAsync();
        var match = roles.FirstOrDefault(r => string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    private static string Enc(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private static string DescribeUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "Unknown device";

        string browser =
            userAgent.Contains("Edg", StringComparison.OrdinalIgnoreCase) ? "Edge" :
            userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
            userAgent.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
            userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari" :
            "Browser";

        string os =
            userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
            userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS" :
            userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
            userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS" :
            userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" :
            "";

        return string.IsNullOrEmpty(os) ? browser : $"{browser} on {os}";
    }

    private static string GetRoleBadge(string roleName)
    {
        return $"<span class='badge {GetRoleBadgeClass(roleName)}'>{Enc(roleName)}</span>";
    }

    private static string GetRoleBadgeClass(string roleName)
    {
        return roleName.ToLower() switch
        {
            "superadmin" => "bg-dark",
            "admin" => "bg-danger",
            "manager" => "bg-warning",
            "cashier" => "bg-info",
            "employee" => "bg-secondary",
            "viewer" => "bg-secondary",
            _ => "bg-secondary"
        };
    }

    private static string GetRoleActionButtons(RoleSummary role)
    {
        // SuperAdmin holds the whole catalog by definition, so there is nothing to edit or remove.
        if (role.IsSuperAdmin)
            return "<span class='badge bg-light text-muted'><i class='fas fa-lock me-1'></i>System</span>";

        var delete = role.IsSystemRole
            ? ""
            : $@"<button type='button' class='btn btn-sm btn-outline-danger' onclick='deleteRole({role.Id})' title='Delete'>
                    <i class='fas fa-trash'></i>
                </button>";

        return $@"
            <div class='btn-group' role='group'>
                <button type='button' class='btn btn-sm btn-outline-primary' onclick='editRole({role.Id})' title='Edit'>
                    <i class='fas fa-edit'></i>
                </button>
                {delete}
            </div>";
    }

    private static string GetUserActionButtons(int userId, bool isActive)
    {
        var toggleIcon = isActive ? "fa-ban" : "fa-check";
        var toggleTitle = isActive ? "Disable" : "Enable";
        var toggleClass = isActive ? "btn-outline-warning" : "btn-outline-success";

        return $@"
            <div class='btn-group' role='group'>
                <button type='button' class='btn btn-sm btn-outline-primary' onclick='editUser({userId})' title='Edit'>
                    <i class='fas fa-edit'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-info' onclick='resetUserPin({userId})' title='Reset PIN'>
                    <i class='fas fa-hashtag'></i>
                </button>
                <button type='button' class='btn btn-sm {toggleClass}' onclick='toggleUserStatus({userId})' title='{toggleTitle}'>
                    <i class='fas {toggleIcon}'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-danger' onclick='deleteUser({userId})' title='Delete'>
                    <i class='fas fa-trash'></i>
                </button>
            </div>";
    }

    #endregion
}

// DTOs
public record CreateUserDto(string Username, string Email, string FirstName, string LastName, string Role, bool IsActive, string? Pin = null);
public record CreateRoleDto(string Name, string? Description, string[]? Permissions);
public record UpdateUserDto(string FirstName, string LastName, string Email, int RoleId, bool IsActive);
public record ResetPinDto(string NewPin);
public record UserStatsDto(int TotalUsers, int TotalRoles, int ActiveUsers, int AdminUsers);
