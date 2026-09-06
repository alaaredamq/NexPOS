using Application.IServices.Products;
using Core.Entities.Products;
using Core.IRepositories.Products;
using Core.Enum;
using Microsoft.Extensions.Logging;

namespace Application.Services.Products;

public class TaxService : ITaxService
{
    private readonly ITaxRepository _taxRepository;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<TaxService> _logger;

    public TaxService(ITaxRepository taxRepository, IProductRepository productRepository, ILogger<TaxService> logger)
    {
        _taxRepository = taxRepository;
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<TaxRate> CreateTaxAsync(TaxRate tax)
    {
        if (tax.IsDefault)
        {
            // If setting as default, first remove default flag from existing taxes
            await _taxRepository.SetDefaultTaxAsync(tax.Id);
        }

        return await _taxRepository.SaveAsync(tax);
    }

    public async Task<TaxRate?> GetTaxByIdAsync(int id)
    {
        return await _taxRepository.GetByIdAsync(id);
    }

    public async Task<List<TaxRate>> GetAllTaxesAsync()
    {
        return await _taxRepository.GetAllAsync();
    }

    public async Task<List<TaxRate>> GetActiveTaxesAsync()
    {
        return await _taxRepository.GetActiveTaxesAsync();
    }

    public async Task<TaxRate?> GetDefaultTaxAsync()
    {
        return await _taxRepository.GetDefaultTaxAsync();
    }

    public async Task<TaxRate?> GetActiveTaxByTypeAsync(TaxType type)
    {
        return await _taxRepository.GetActiveTaxAsync(type);
    }

    public async Task<List<TaxRate>> GetTaxesByCategoryAsync(int categoryId)
    {
        return await _taxRepository.GetTaxesByCategoryAsync(categoryId);
    }

    public async Task<List<TaxRate>> GetEffectiveTaxesAsync(DateTime date)
    {
        return await _taxRepository.GetEffectiveTaxesAsync(date);
    }

    public async Task<TaxRate> UpdateTaxAsync(int id, TaxRate tax)
    {
        if (tax.IsDefault)
        {
            // If setting as default, first remove default flag from existing taxes
            await _taxRepository.SetDefaultTaxAsync(id);
        }

        return await _taxRepository.SaveAsync(tax);
    }

    public async Task<bool> DeleteTaxAsync(int id)
    {
        await _taxRepository.DeleteAsync(id);
        return true;
    }

    public async Task<bool> SetDefaultTaxAsync(int taxId)
    {
        return await _taxRepository.SetDefaultTaxAsync(taxId);
    }

    public async Task<bool> DeactivateTaxAsync(int taxId)
    {
        return await _taxRepository.DeactivateTaxAsync(taxId);
    }

    public async Task<List<TaxRate>> SearchTaxesAsync(string searchTerm)
    {
        return await _taxRepository.SearchTaxesAsync(searchTerm);
    }

    // Tax calculation methods

    public async Task<decimal> CalculateTaxAmountAsync(decimal baseAmount, int? taxId = null, TaxType? taxType = null)
    {
        TaxRate? tax = null;

        if (taxId.HasValue)
        {
            tax = await _taxRepository.GetByIdAsync(taxId.Value);
        }
        else if (taxType.HasValue)
        {
            tax = await _taxRepository.GetActiveTaxAsync(taxType.Value);
        }
        else
        {
            tax = await _taxRepository.GetDefaultTaxAsync();
        }

        if (tax == null || !tax.IsEffectiveAt(DateTime.UtcNow))
        {
            return 0;
        }

        return tax.CalculateTaxAmount(baseAmount);
    }

    public async Task<decimal> CalculateAmountWithTaxAsync(decimal baseAmount, int? taxId = null, TaxType? taxType = null)
    {
        TaxRate? tax = null;

        if (taxId.HasValue)
        {
            tax = await _taxRepository.GetByIdAsync(taxId.Value);
        }
        else if (taxType.HasValue)
        {
            tax = await _taxRepository.GetActiveTaxAsync(taxType.Value);
        }
        else
        {
            tax = await _taxRepository.GetDefaultTaxAsync();
        }

        if (tax == null || !tax.IsEffectiveAt(DateTime.UtcNow))
        {
            return baseAmount;
        }

        return tax.CalculateAmountWithTax(baseAmount);
    }

    public async Task<decimal> CalculateAmountExcludingTaxAsync(decimal amountWithTax, int? taxId = null, TaxType? taxType = null)
    {
        TaxRate? tax = null;

        if (taxId.HasValue)
        {
            tax = await _taxRepository.GetByIdAsync(taxId.Value);
        }
        else if (taxType.HasValue)
        {
            tax = await _taxRepository.GetActiveTaxAsync(taxType.Value);
        }
        else
        {
            tax = await _taxRepository.GetDefaultTaxAsync();
        }

        if (tax == null || !tax.IsEffectiveAt(DateTime.UtcNow))
        {
            return amountWithTax;
        }

        return tax.CalculateAmountExcludingTax(amountWithTax);
    }

    public async Task<TaxCalculationResult> CalculateTaxForProductAsync(int productId, decimal baseAmount)
    {
        var product = await _productRepository.GetByIdAsync(productId);
        if (product == null)
        {
            throw new ArgumentException($"Product with ID {productId} not found.");
        }

        // Get applicable taxes for the product's category
        var applicableTaxes = await _taxRepository.GetTaxesByCategoryAsync(product.CategoryId);

        if (!applicableTaxes.Any())
        {
            // Fall back to default tax
            var defaultTax = await _taxRepository.GetDefaultTaxAsync();
            if (defaultTax == null)
            {
                return new TaxCalculationResult
                {
                    TaxId = 0,
                    TaxName = "No Tax",
                    TaxType = TaxType.CustomTax,
                    TaxRate = 0,
                    BaseAmount = baseAmount,
                    TaxAmount = 0,
                    TotalAmount = baseAmount,
                    IsInclusive = false
                };
            }

            applicableTaxes = new List<TaxRate> { defaultTax };
        }

        // For now, use the first applicable tax (could be enhanced to support multiple taxes)
        var tax = applicableTaxes.First();
        
        return new TaxCalculationResult
        {
            TaxId = tax.Id,
            TaxName = tax.Name,
            TaxType = tax.Type,
            TaxRate = tax.Rate,
            BaseAmount = baseAmount,
            TaxAmount = tax.CalculateTaxAmount(baseAmount),
            TotalAmount = tax.CalculateAmountWithTax(baseAmount),
            IsInclusive = tax.IsInclusive
        };
    }

    public async Task<List<TaxCalculationResult>> CalculateTaxesForOrderAsync(List<OrderTaxItem> orderItems)
    {
        var results = new List<TaxCalculationResult>();

        foreach (var item in orderItems)
        {
            TaxRate? tax = null;

            // Check if a specific tax is specified
            if (item.SpecificTaxId.HasValue)
            {
                tax = await _taxRepository.GetByIdAsync(item.SpecificTaxId.Value);
            }
            else if (item.CategoryId.HasValue)
            {
                // Get taxes for the category
                var categoryTaxes = await _taxRepository.GetTaxesByCategoryAsync(item.CategoryId.Value);
                tax = categoryTaxes.FirstOrDefault();
            }

            // Fall back to default tax if no specific tax found
            if (tax == null)
            {
                tax = await _taxRepository.GetDefaultTaxAsync();
            }

            if (tax != null && tax.IsEffectiveAt(DateTime.UtcNow))
            {
                results.Add(new TaxCalculationResult
                {
                    TaxId = tax.Id,
                    TaxName = tax.Name,
                    TaxType = tax.Type,
                    TaxRate = tax.Rate,
                    BaseAmount = item.BaseAmount,
                    TaxAmount = tax.CalculateTaxAmount(item.BaseAmount),
                    TotalAmount = tax.CalculateAmountWithTax(item.BaseAmount),
                    IsInclusive = tax.IsInclusive
                });
            }
            else
            {
                results.Add(new TaxCalculationResult
                {
                    TaxId = 0,
                    TaxName = "No Tax",
                    TaxType = TaxType.CustomTax,
                    TaxRate = 0,
                    BaseAmount = item.BaseAmount,
                    TaxAmount = 0,
                    TotalAmount = item.BaseAmount,
                    IsInclusive = false
                });
            }
        }

        return results;
    }
}
