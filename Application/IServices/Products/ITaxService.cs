using Application.IServices.Base;
using Core.Entities.Products;
using Core.Enum;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Application.IServices.Products;

public interface ITaxService : IBaseService
{
    Task<TaxRate> CreateTaxAsync(TaxRate tax);
    Task<TaxRate?> GetTaxByIdAsync(int id);
    Task<List<TaxRate>> GetAllTaxesAsync();
    Task<List<TaxRate>> GetActiveTaxesAsync();
    Task<TaxRate?> GetDefaultTaxAsync();
    Task<TaxRate?> GetActiveTaxByTypeAsync(TaxType type);
    Task<List<TaxRate>> GetTaxesByCategoryAsync(int categoryId);
    Task<List<TaxRate>> GetEffectiveTaxesAsync(DateTime date);
    Task<TaxRate> UpdateTaxAsync(int id, TaxRate tax);
    Task<bool> DeleteTaxAsync(int id);
    Task<bool> SetDefaultTaxAsync(int taxId);
    Task<bool> DeactivateTaxAsync(int taxId);
    Task<List<TaxRate>> SearchTaxesAsync(string searchTerm);
    
    // Tax calculation methods
    Task<decimal> CalculateTaxAmountAsync(decimal baseAmount, int? taxId = null, TaxType? taxType = null);
    Task<decimal> CalculateAmountWithTaxAsync(decimal baseAmount, int? taxId = null, TaxType? taxType = null);
    Task<decimal> CalculateAmountExcludingTaxAsync(decimal amountWithTax, int? taxId = null, TaxType? taxType = null);
    Task<TaxCalculationResult> CalculateTaxForProductAsync(int productId, decimal baseAmount);
    Task<List<TaxCalculationResult>> CalculateTaxesForOrderAsync(List<OrderTaxItem> orderItems);
}

public class TaxCalculationResult
{
    public int TaxId { get; set; }
    public string TaxName { get; set; } = null!;
    public TaxType TaxType { get; set; }
    public decimal TaxRate { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsInclusive { get; set; }
}

public class OrderTaxItem
{
    public int ProductId { get; set; }
    public int? CategoryId { get; set; }
    public decimal BaseAmount { get; set; }
    public int? SpecificTaxId { get; set; }
}
