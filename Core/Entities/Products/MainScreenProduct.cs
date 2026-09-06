using Core.Entities.Base;

namespace Core.Entities.Products;

public class MainScreenProduct : IEntityBase<int>
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
