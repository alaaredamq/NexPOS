using System.ComponentModel.DataAnnotations;

namespace Web.Api.Account.Contracts;

/// <summary>The signed-in user's own account page.</summary>
public sealed record ProfileDto
{
    public required int Id { get; init; }

    public required string Username { get; init; }

    public required string Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public required string FullName { get; init; }

    public required int RoleId { get; init; }

    public required string RoleName { get; init; }

    /// <summary>UTC; null if never.</summary>
    public DateTime? LastLoginAt { get; init; }

    /// <summary>An admin set the PIN: <see cref="ChangePinRequest.CurrentPin"/> is not asked for until it has been replaced.</summary>
    public required bool MustChangePin { get; init; }
}

/// <summary>Body of "change my PIN".</summary>
public sealed record ChangePinRequest
{
    /// <summary>Required, unless an admin set the PIN and it has not been changed yet (<see cref="ProfileDto.MustChangePin"/>): the user has just signed in with it.</summary>
    [RegularExpression(@"^\d{4}$")]
    public string? CurrentPin { get; init; }

    [Required]
    [RegularExpression(@"^\d{4}$")]
    public required string NewPin { get; init; }

    [Required]
    public required string ConfirmPin { get; init; }
}
