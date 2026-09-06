namespace Application.DTOs.Customers;

public class CustomerResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public string? CompanyName { get; set; }
    public string? TaxNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Computed properties
    public int Age => DateOfBirth.HasValue ? DateTime.Now.Year - DateOfBirth.Value.Year : 0;
    public bool IsCompany => !string.IsNullOrEmpty(CompanyName);
    public int TotalOrders { get; set; } = 0;
    public decimal TotalSpent { get; set; } = 0;
    public decimal AverageOrderValue => TotalOrders > 0 ? TotalSpent / TotalOrders : 0;
}
