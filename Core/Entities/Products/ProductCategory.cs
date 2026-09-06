using Core.Entities.Base;

namespace Core.Entities.Products;

public class ProductCategory: BaseEntity, IEntityBase<int>
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    #region Navigation Properties

    public ICollection<Product> Products { get; set; } = new List<Product>();

    #endregion

    #region Constructors

    public ProductCategory(string name, string? description = null)
    {
        Name = name;
        Description = description;
        IsActive = true;
    }

    #endregion
}