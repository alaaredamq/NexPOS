using System.Security.Claims;
using Application.IServices.Licensing;
using Application.IServices.Notifications;
using Application.IServices.Sales;
using Application.IServices.Users;
using Microsoft.Extensions.Localization;
using Web.Api.Session.Contracts;
using Web.Helpers;
using Web.Services;

namespace Web.Api.Session;

/// <summary>Builds the shell's whole bootstrap document for a signed-in principal.</summary>
public interface ISessionBootstrapService
{
    /// <param name="principal">The signed-in user; for a request that has just signed in, the principal that was issued, not <c>HttpContext.User</c>.</param>
    /// <param name="language"><c>en</c> or <c>ar</c>.</param>
    /// <param name="mustChangePin">Known by the caller right after sign-in; null looks it up.</param>
    Task<SessionDto> BuildAsync(ClaimsPrincipal principal, string language, bool? mustChangePin = null);
}

public sealed class SessionBootstrapService : ISessionBootstrapService
{
    private readonly IUserService _users;
    private readonly ILicenseService _license;
    private readonly IBrandingService _branding;
    private readonly ICashDrawerService _drawer;
    private readonly INotificationService _notifications;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<SessionBootstrapService> _logger;

    public SessionBootstrapService(
        IUserService users, ILicenseService license, IBrandingService branding, ICashDrawerService drawer,
        INotificationService notifications, IStringLocalizer<SharedResource> localizer, ILogger<SessionBootstrapService> logger)
    {
        _users = users;
        _license = license;
        _branding = branding;
        _drawer = drawer;
        _notifications = notifications;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<SessionDto> BuildAsync(ClaimsPrincipal principal, string language, bool? mustChangePin = null)
    {
        _ = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var fullName = principal.FindFirstValue("FullName") is { Length: > 0 } name ? name : principal.Identity?.Name ?? string.Empty;

        return new SessionDto
        {
            User = new SessionUserDto
            {
                Id = userId,
                Username = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                FullName = fullName,
                Email = principal.FindFirstValue(ClaimTypes.Email) is { Length: > 0 } email ? email : null,
                Initials = InitialsOf(fullName),
                MustChangePin = mustChangePin ?? await MustChangePinAsync(userId),
            },
            Role = new SessionRoleDto
            {
                Id = int.TryParse(principal.FindFirstValue("RoleId"), out var roleId) ? roleId : 0,
                Name = principal.FindFirstValue("Role") ?? string.Empty,
            },
            Permissions = principal.FindAll("Permission").Select(c => c.Value).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList(),
            Nav = BuildNav(principal, language),
            License = BuildLicense(),
            Branding = BuildBranding(_branding),
            Language = language,
            Direction = language == "ar" ? "rtl" : "ltr",
            Drawer = await BuildDrawerAsync(principal, userId),
            UnreadNotifications = await UnreadAsync(),
        };
    }

    private List<NavSectionDto> BuildNav(ClaimsPrincipal principal, string language)
    {
        var visible = NavCatalog.VisibleTo(principal);
        var sections = new List<NavSectionDto>();

        foreach (var section in NavCatalog.Sections)
        {
            var items = visible.Where(p => p.Section == section).Select(page =>
            {
                var migrated = SpaMigration.IsMigrated(page.Controller);
                return new NavItemDto
                {
                    Key = page.Controller,
                    LabelKey = page.LabelKey,
                    Label = _localizer[page.LabelKey].Value,
                    Icon = page.Icon,
                    Permissions = page.Permissions,
                    AlsoActiveOn = page.AlsoActiveOn ?? Array.Empty<string>(),
                    SpaPath = SpaMigration.PlannedPath(page.Controller),
                    LegacyPath = SpaMigration.LegacyPath(page.Controller, language),
                    Migrated = migrated,
                    Href = SpaMigration.HrefFor(page.Controller, language),
                };
            }).ToList();

            if (items.Count == 0) continue; // a section the user has nothing in is not shown, as in the Razor rail

            var labelKey = NavCatalog.SectionLabelKey(section);
            sections.Add(new NavSectionDto { Key = section.ToString(), LabelKey = labelKey, Label = _localizer[labelKey].Value, Items = items });
        }

        return sections;
    }

    private LicenseSummaryDto BuildLicense()
    {
        var state = _license.GetState();
        return new LicenseSummaryDto
        {
            Status = state.Status.ToString(),
            AllowsSales = state.AllowsSales,
            ShowsWarning = state.ShowsWarning,
            DaysLeft = state.DaysLeft,
            DaysUntilStop = state.DaysUntilStop,
            ValidUntil = state.ValidUntil?.ToString("yyyy-MM-dd"),
            EnforcementDisabled = state.EnforcementDisabled,
            Title = LicenseMessageHelper.Title(state, _localizer),
            Message = LicenseMessageHelper.Message(state, _localizer),
            Severity = LicenseMessageHelper.Severity(state),
        };
    }

    /// <summary>The branding block of the session; also served to the login screen (before anyone is signed in).</summary>
    internal static BrandingDto BuildBranding(IBrandingService branding)
    {
        var version = Uri.EscapeDataString(branding.Version);
        return new BrandingDto
        {
            AppName = branding.AppName,
            HasCustomLogo = branding.HasCustomLogo,
            LogoUrl = $"/branding/logo?v={version}",
            MarkUrl = $"/branding/logo?variant=mark&v={version}",
            FaviconUrl = $"/branding/favicon?v={version}",
        };
    }

    // Same rule as the Razor drawer chip: only for people who can use a drawer, and "unknown" (null) rather than a failed page.
    private async Task<DrawerStatusDto?> BuildDrawerAsync(ClaimsPrincipal principal, int userId)
    {
        if (!PermissionHelper.HasPermission(principal, PermissionHelper.Permissions.Sales_View) || userId == 0) return null;

        try
        {
            var open = await _drawer.GetOpenSessionByUserIdAsync(userId);
            return new DrawerStatusDto
            {
                IsOpen = open is not null,
                SessionId = open?.Id,
                OpeningCash = open?.OpeningCash ?? 0m,
                OpenedAt = open?.OpenedAt,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session: could not read the cash drawer of user {UserId}", userId);
            return null;
        }
    }

    private async Task<int> UnreadAsync()
    {
        try
        {
            return await _notifications.GetUnreadCountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session: could not count unread notifications");
            return 0;
        }
    }

    private async Task<bool> MustChangePinAsync(int userId)
    {
        if (userId == 0) return false;

        try
        {
            return (await _users.GetByIdAsync(userId)).MustChangePin;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session: could not read the user {UserId}", userId);
            return false;
        }
    }

    internal static string InitialsOf(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }
}
