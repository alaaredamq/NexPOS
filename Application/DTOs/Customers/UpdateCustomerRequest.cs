using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Customers;

public class UpdateCustomerRequest
{
    [Required]
    public int Id { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [StringLength(20)]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [StringLength(500)]
    [Display(Name = "Address")]
    public string? Address { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
    public DateTime? DateOfBirth { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [StringLength(100)]
    [Display(Name = "Company Name")]
    public string? CompanyName { get; set; }

    [StringLength(50)]
    [Display(Name = "Tax Number")]
    public string? TaxNumber { get; set; }
}
