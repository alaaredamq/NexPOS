using System.ComponentModel.DataAnnotations;

namespace Web.Models.Auth;

public class LoginViewModel
{
    [Required(ErrorMessage = "Username is required")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    // Kept for the legacy JWT API path (AuthController) - the actual web login (AccountController)
    // uses Pin instead, per Q2's "PIN for all roles, single login path" decision.
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "PIN is required")]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be exactly 4 digits")]
    [DataType(DataType.Password)]
    [Display(Name = "PIN")]
    public string Pin { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class LoginResultViewModel
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Message { get; set; }
    public string? ReturnUrl { get; set; }
}
