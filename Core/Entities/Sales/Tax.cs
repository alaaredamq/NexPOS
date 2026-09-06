using Core.Entities.Base;

namespace Core.Entities.Sales;

public class Tax : ISSoftDeletable, IEntityBase<int>
{
    public string Name { get; set; } = null!;
    public decimal Rate { get; set; }
    public bool IsDefault { get; set; }
    public int? SupplierId { get; set; }

    #region Navigation Properties

    public Products.Supplier? Supplier { get; set; }

    #endregion

    public Tax(string name, decimal rate, bool isDefault = false, int? supplierId = null)
    {
        Name = name;
        Rate = rate;
        IsDefault = isDefault;
        SupplierId = supplierId;
    }

    protected Tax() { }
}


