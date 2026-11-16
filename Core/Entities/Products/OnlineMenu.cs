using Core.Entities.Base;

namespace Core.Entities.Products;

/// <summary>
/// A heading on the shop's online menu ("Breakfast", "Today's specials"). Separate from product categories on purpose: categories
/// organise stock, sections organise what customers read. When at least one section exists, the online menu shows exactly what the
/// sections hold, in their order; with none, it falls back to the product categories.
/// </summary>
public class OnlineMenuSection : BaseEntity, IEntityBase<int>
{
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }

    public ICollection<OnlineMenuEntry> Entries { get; set; } = new List<OnlineMenuEntry>();
}

/// <summary>
/// What a section shows: either one product, or a whole category (every active product in it, including ones added later).
/// Exactly one of <see cref="ProductId"/> and <see cref="CategoryId"/> is set. A product appears in one section only: a product picked
/// on its own wins over its category, and the first section (in menu order) wins over later ones.
/// </summary>
public class OnlineMenuEntry : BaseEntity, IEntityBase<int>
{
    public int SectionId { get; set; }
    public OnlineMenuSection Section { get; set; } = null!;
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? CategoryId { get; set; }
    public ProductCategory? Category { get; set; }
    public int SortOrder { get; set; }
}
