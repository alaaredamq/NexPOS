using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Customers;

public class AddCustomerDebtTransactionRequest
{
    [Required]
    public int CustomerId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    /// <summary>
    /// true: the customer owes the business more (balance increases).
    /// false: the business owes the customer more, or the customer is paying down what they owe (balance decreases).
    /// </summary>
    public bool CustomerOwesMore { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
