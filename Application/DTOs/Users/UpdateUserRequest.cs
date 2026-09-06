using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Users;

public class UpdateUserRequest
{
    [Required]
    public int Id { get; set; }

    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [StringLength(100)]
    public string? FirstName { get; set; }

    [StringLength(100)]
    public string? LastName { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Role is required")]
    public int RoleId { get; set; }

    public bool IsActive { get; set; } = true;

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }

    public DateTime? DateOfBirth { get; set; }
}
