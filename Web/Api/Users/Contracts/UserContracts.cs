using System.ComponentModel.DataAnnotations;

namespace Web.Api.Users.Contracts;

/// <summary>A user as the Users page lists and edits them. Credentials (PIN, password hash) never leave the server.</summary>
public sealed record UserDto
{
    public required int Id { get; init; }

    public required string Username { get; init; }

    public required string Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>First and last name, or the username while either is missing (the same rule as the rest of the app).</summary>
    public required string FullName { get; init; }

    public required int RoleId { get; init; }

    public required string RoleName { get; init; }

    /// <summary>A disabled user cannot sign in, and a signed-in one is turned away on their next request.</summary>
    public required bool IsActive { get; init; }

    /// <summary>The PIN was set by an admin and the user has not replaced it yet.</summary>
    public required bool MustChangePin { get; init; }

    /// <summary>UTC; null for someone who never signed in.</summary>
    public DateTime? LastLoginAt { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>The four tiles above the Users page (all users and roles, not just the current page).</summary>
public sealed record UserSummaryDto
{
    public required int TotalUsers { get; init; }

    public required int TotalRoles { get; init; }

    public required int ActiveUsers { get; init; }

    /// <summary>Users whose role holds <c>Users.Manage</c>: "admin" is whoever can manage users and roles, role names are the store's to choose.</summary>
    public required int AdminUsers { get; init; }
}

/// <summary>Body of "create a user". The PIN is the account's only credential and the user must change it at first sign-in.</summary>
public sealed record UserCreateRequest
{
    /// <summary>Trimmed; no spaces. The sign-in screen picks users by this name.</summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Username { get; init; }

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public required string Email { get; init; }

    [StringLength(100)]
    public string? FirstName { get; init; }

    [StringLength(100)]
    public string? LastName { get; init; }

    public required int RoleId { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Exactly 4 digits (the sign-in keypad takes nothing else).</summary>
    [Required]
    [RegularExpression(@"^\d{4}$")]
    public required string Pin { get; init; }
}

/// <summary>Body of "edit a user". The username cannot be changed.</summary>
public sealed record UserUpdateRequest
{
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public required string Email { get; init; }

    [StringLength(100)]
    public string? FirstName { get; init; }

    [StringLength(100)]
    public string? LastName { get; init; }

    public required int RoleId { get; init; }

    public required bool IsActive { get; init; }
}

/// <summary>Body of "reset a user's PIN": the user must pick their own at the next sign-in.</summary>
public sealed record UserResetPinRequest
{
    [Required]
    [RegularExpression(@"^\d{4}$")]
    public required string NewPin { get; init; }
}

/// <summary>Body of "enable / disable a user".</summary>
public sealed record UserActiveRequest
{
    public required bool IsActive { get; init; }
}
