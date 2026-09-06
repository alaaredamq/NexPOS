using Core.Entities.Base;

namespace Core.Entities.Sales;

public class PaymentMethod : ISSoftDeletable, IEntityBase<int>
{
    public string Name { get; set; } = null!;
    public bool IsDefault { get; set; }
    public int? SupplierId { get; set; }

    #region Navigation Properties

    public Products.Supplier? Supplier { get; set; }

    #endregion

    public PaymentMethod(string name, bool isDefault = false, int? supplierId = null)
    {
        Name = name;
        IsDefault = isDefault;
        SupplierId = supplierId;
    }

    protected PaymentMethod() { }
}


