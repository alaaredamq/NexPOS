using Core.Entities.Products;

namespace Core.IRepositories;

/// <summary>Where one pick sits, without loading its product or category (for counts and "where is it now").</summary>
public sealed record OnlineMenuPlacement(int EntryId, int SectionId, int? ProductId, int? CategoryId);

/// <summary>
/// The online menu's sections and what they hold (see <see cref="OnlineMenuSection"/>). A shop can have thousands of products, so
/// nothing here loads every pick with its product: sections come without entries, and entries come a page at a time.
/// </summary>
public interface IOnlineMenuRepository
{
    /// <summary>Every section in menu order, without entries.</summary>
    Task<List<OnlineMenuSection>> GetSectionsAsync();

    /// <summary>Every pick as plain ids (cheap: no products or categories loaded).</summary>
    Task<List<OnlineMenuPlacement>> GetPlacementsAsync();

    /// <summary>One page of a section's picks in order, with their product (and currency) or category.</summary>
    Task<(List<OnlineMenuEntry> Items, bool HasMore)> GetEntriesAsync(int sectionId, int skip, int take);

    /// <summary>For the given product ids: their category and whether they are active and not deleted.</summary>
    Task<Dictionary<int, (int CategoryId, bool Active)>> GetProductStatesAsync(IReadOnlyCollection<int> productIds);

    Task<OnlineMenuSection> AddSectionAsync(string name);

    /// <summary>False when there is no such section.</summary>
    Task<bool> RenameSectionAsync(int id, string name);

    /// <summary>False when there is no such section. Its entries go with it; the products themselves are untouched.</summary>
    Task<bool> DeleteSectionAsync(int id);

    /// <summary>The listed sections first, in that order; any not listed keep their relative order after them.</summary>
    Task ReorderSectionsAsync(IReadOnlyList<int> ids);

    /// <summary>
    /// Puts products and whole categories at the end of a section. One already placed in another section moves here. Unknown ids
    /// are skipped. Returns false when there is no such section.
    /// </summary>
    Task<bool> PlaceAsync(int sectionId, IReadOnlyCollection<int> productIds, IReadOnlyCollection<int> categoryIds);

    /// <summary>False when there is no such entry.</summary>
    Task<bool> RemoveEntryAsync(int entryId);

    /// <summary>Swaps a pick with its neighbour in its section (-1 = up, +1 = down). False when there is no such entry.</summary>
    Task<bool> MoveEntryAsync(int entryId, int direction);

    /// <summary>Active products whose name or barcode contains <paramref name="term"/>, by name, with category and currency.</summary>
    Task<(List<Product> Items, bool HasMore)> SearchProductsAsync(string? term, int skip, int take);

    /// <summary>Every category by name, with how many active products it has.</summary>
    Task<List<(ProductCategory Category, int ActiveProducts)>> GetCategoriesAsync();
}
