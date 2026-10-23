using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Suppliers;

public class AddSupplierDebtTransactionRequest
{
    [Required]
    public int SupplierId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    /// <summary>
    /// true: the business owes the supplier more (balance increases).
    /// false: the business is paying down what it owes, or the supplier owes the business (balance decreases).
    /// </summary>
    public bool IOweMore { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
