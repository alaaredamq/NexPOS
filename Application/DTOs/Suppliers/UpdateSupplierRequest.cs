using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Suppliers;

public class UpdateSupplierRequest
{
    [Required]
    public int Id { get; set; }

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

    public bool IsActive { get; set; } = true;
}

