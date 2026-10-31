namespace Web.Api.Session.Contracts;

/// <summary>
/// Everything the shell needs to draw itself, in one round trip: <c>GET /api/v1/session</c> on boot (and after
/// <c>POST /api/v1/auth/login</c>, which returns the same document).
/// </summary>
public sealed record SessionDto
{
    public required SessionUserDto User { get; init; }

    public required SessionRoleDto Role { get; init; }

    /// <summary>
    /// The permission keys the user holds right now (e.g. <c>Orders.View</c>), re-read from the database on every request.
    /// The full catalogue is generated into <c>client/src/api/permissions.ts</c>.
    /// </summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>The rail / Ctrl+K palette, already filtered to what the user may open, grouped by section, in rail order.</summary>
    public required IReadOnlyList<NavSectionDto> Nav { get; init; }

    public required LicenseSummaryDto License { get; init; }

    public required BrandingDto Branding { get; init; }

    /// <summary>The language this response was localized in: <c>en</c> or <c>ar</c>.</summary>
    public required string Language { get; init; }

    /// <summary><c>ltr</c> or <c>rtl</c>, for <c>&lt;html dir&gt;</c>.</summary>
    public required string Direction { get; init; }

    /// <summary>The user's cash drawer; null when they cannot use one (no <c>Sales.View</c>) or it could not be read.</summary>
    public DrawerStatusDto? Drawer { get; init; }

    /// <summary>Unread notifications, for the bell badge. 0 when it could not be read.</summary>
    public required int UnreadNotifications { get; init; }
}

public sealed record SessionUserDto
{
    public required int Id { get; init; }

    public required string Username { get; init; }

    public required string FullName { get; init; }

    public string? Email { get; init; }

    /// <summary>Up to two letters for the avatar.</summary>
    public required string Initials { get; init; }

    /// <summary>The PIN was set by an admin; the UI sends the user to change it before anything else.</summary>
    public required bool MustChangePin { get; init; }
}

public sealed record SessionRoleDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }
}

public sealed record NavSectionDto
{
    /// <summary><c>Sales</c>, <c>Catalog</c>, <c>Insights</c> or <c>Admin</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Key in the shared resources / the SPA's <c>common</c> namespace.</summary>
    public required string LabelKey { get; init; }

    /// <summary>Already localized for this response.</summary>
    public required string Label { get; init; }

    public required IReadOnlyList<NavItemDto> Items { get; init; }
}

public sealed record NavItemDto
{
    /// <summary>The page's MVC controller name (<c>Orders</c>, <c>CashDrawer</c>): the stable id of a page.</summary>
    public required string Key { get; init; }

    public required string LabelKey { get; init; }

    public required string Label { get; init; }

    /// <summary>Font Awesome class of the Razor rail (<c>fa-box</c>); the SPA maps it to its own icon set.</summary>
    public required string Icon { get; init; }

    /// <summary>Any one of these grants the page; empty means every signed-in user.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>Other controllers whose pages light this entry up.</summary>
    public required IReadOnlyList<string> AlsoActiveOn { get; init; }

    /// <summary>The path the SPA registers this page under, migrated or not.</summary>
    public required string SpaPath { get; init; }

    /// <summary>The Razor page in the current language.</summary>
    public required string LegacyPath { get; init; }

    /// <summary>True when the React page is finished and <see cref="Href"/> points at it.</summary>
    public required bool Migrated { get; init; }

    /// <summary>Where a link should go right now: <see cref="SpaPath"/> when migrated, otherwise <see cref="LegacyPath"/> (full navigation, same cookie).</summary>
    public required string Href { get; init; }
}

public sealed record LicenseSummaryDto
{
    /// <summary>Active, ExpiringSoon, GracePeriod, Expired, NotActivated, Invalid, WrongMachine or UpdatesNotCovered.</summary>
    public required string Status { get; init; }

    /// <summary>False means the POS and new sales are stopped; everything else keeps working.</summary>
    public required bool AllowsSales { get; init; }

    /// <summary>Show the renewal banner (ExpiringSoon or GracePeriod).</summary>
    public required bool ShowsWarning { get; init; }

    /// <summary>ExpiringSoon only: days until the licence expires (0 = today).</summary>
    public int? DaysLeft { get; init; }

    /// <summary>GracePeriod only: days until selling stops (0 = today is the last day).</summary>
    public int? DaysUntilStop { get; init; }

    public string? ValidUntil { get; init; }

    /// <summary>A Debug build: nothing is enforced.</summary>
    public required bool EnforcementDisabled { get; init; }
}

public sealed record BrandingDto
{
    public required string AppName { get; init; }

    public required bool HasCustomLogo { get; init; }

    /// <summary>Full logo; the SPA adds a <c>?variant=mark|light|icon</c> for the others. Cache-busted with the branding version.</summary>
    public required string LogoUrl { get; init; }

    public required string MarkUrl { get; init; }

    public required string FaviconUrl { get; init; }
}

public sealed record DrawerStatusDto
{
    public required bool IsOpen { get; init; }

    public int? SessionId { get; init; }

    /// <summary>Opening cash of the open session; 0 when closed.</summary>
    public required decimal OpeningCash { get; init; }

    public DateTime? OpenedAt { get; init; }
}
