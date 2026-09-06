using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Suppliers;

public class CreateSupplierRequest
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; set; }

    [Phone]
    [StringLength(20)]
    public string? Phone { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(20)]
    public string? PostalCode { get; set; }

    [Range(0, 100)]
    public double DiscountRate { get; set; } = 0;

    [Required]
    public int CurrencyId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Debt amount must be positive")]
    public decimal DebtToSupplier { get; set; } = 0; // Money I owe the supplier

    [Range(0, double.MaxValue, ErrorMessage = "Debt amount must be positive")]
    public decimal DebtFromSupplier { get; set; } = 0; // Money the supplier owes me

    public bool IsActive { get; set; } = true;
}

