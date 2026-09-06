using Core.Entities.Base;
using Core.Enum;

namespace Core.Entities.Products;

public class TaxRate : ISSoftDeletable, IEntityBase<int>
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal Rate { get; set; }
    public TaxType Type { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? TaxCode { get; set; }
    public bool IsInclusive { get; set; } // Whether tax is included in the price
    public int? CategoryId { get; set; } // Optional: tax specific to a category
    public bool ApplyToAllProducts { get; set; } = true;

    #region Navigation Properties

    public ProductCategory? Category { get; set; }

    #endregion

    #region Constructors

    public TaxRate(string name, decimal rate, TaxType type, bool isDefault = false, bool isActive = true)
    {
        Name = name;
        Rate = rate;
        Type = type;
        IsDefault = isDefault;
        IsActive = isActive;
        EffectiveFrom = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }

    protected TaxRate() { } // Required for EF Core

    #endregion

    #region Methods

    public decimal CalculateTaxAmount(decimal baseAmount)
    {
        return baseAmount * (Rate / 100);
    }

    public decimal CalculateAmountWithTax(decimal baseAmount)
    {
        return baseAmount + CalculateTaxAmount(baseAmount);
    }

    public decimal CalculateAmountExcludingTax(decimal amountWithTax)
    {
        if (IsInclusive)
        {
            return amountWithTax / (1 + (Rate / 100));
        }
        return amountWithTax;
    }

    public bool IsEffectiveAt(DateTime date)
    {
        if (!IsActive) return false;
        
        if (EffectiveFrom.HasValue && date < EffectiveFrom.Value) return false;
        if (EffectiveTo.HasValue && date > EffectiveTo.Value) return false;
        
        return true;
    }

    public void SetEffectivePeriod(DateTime? from, DateTime? to)
    {
        EffectiveFrom = from;
        EffectiveTo = to;
    }

    public void Deactivate()
    {
        IsActive = false;
        EffectiveTo = DateTime.UtcNow;
    }

    #endregion
}
