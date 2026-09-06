using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Customers;

public class CreateCustomerRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [DataType(DataType.Date)]
    public DateTime? DateOfBirth { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(1000)]
    public string? Notes { get; set; }

    [StringLength(100)]
    public string? CompanyName { get; set; }

    [StringLength(50)]
    public string? TaxNumber { get; set; }
}
