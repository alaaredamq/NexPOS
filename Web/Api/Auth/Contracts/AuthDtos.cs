using System.ComponentModel.DataAnnotations;

namespace Web.Api.Auth.Contracts;

/// <summary>PIN sign-in, exactly the fields of the Razor login page.</summary>
public sealed class LoginRequest
{
    [Required(ErrorMessage = "Username is required")]
    public string Username { get; set; } = string.Empty;

    /// <summary>The 4-digit PIN.</summary>
    [Required(ErrorMessage = "PIN is required")]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be exactly 4 digits")]
    public string Pin { get; set; } = string.Empty;

    /// <summary>Keep the cookie for 30 days instead of 8 hours.</summary>
    public bool RememberMe { get; set; }
}

/// <summary>What the (unauthenticated) sign-in screen may know: who can be picked, and who signed in last on this PC.</summary>
public sealed record SignInOptionsDto
{
    public required IReadOnlyList<SignInUserDto> Users { get; init; }

    /// <summary>The remembered user, if they are still active; otherwise empty (the screen opens on the picker).</summary>
    public required string LastUsername { get; init; }

    /// <summary>App name and logo for the login screen (the session payload carries the same block once signed in).</summary>
    public required Web.Api.Session.Contracts.BrandingDto Branding { get; init; }
}

public sealed record SignInUserDto
{
    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string Initials { get; init; }
}
