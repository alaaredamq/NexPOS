using Core.Entities.Products;
using Core.IRepositories;
using Sync;
using Web.Api.OnlineMenu.Contracts;

namespace Web.Api.OnlineMenu;

public interface IOnlineMenuApiService
{
    /// <summary>The sections with their counts and the menu's status. Picks are not included: see <see cref="EntriesAsync"/>.</summary>
    Task<OnlineMenuDto> GetAsync();

    /// <summary>One page of a section's picks; null when there is no such section.</summary>
    Task<OnlineMenuEntriesDto?> EntriesAsync(int sectionId, int skip, int take);

    Task<OnlineMenuSectionDto> AddSectionAsync(string name);

    Task<bool> RenameSectionAsync(int id, string name);

    Task<bool> DeleteSectionAsync(int id);

    Task ReorderSectionsAsync(IReadOnlyList<int> ids);

    Task<bool> PlaceAsync(int sectionId, IReadOnlyCollection<int> productIds, IReadOnlyCollection<int> categoryIds);

    Task<bool> RemoveEntryAsync(int entryId);

    Task<bool> MoveEntryAsync(int entryId, int direction);

    Task<OnlineMenuProductOptionsDto> ProductsAsync(string? term, int skip, int take);

    Task<IReadOnlyList<OnlineMenuCategoryOptionDto>> CategoriesAsync();

    /// <summary>Asks the sync service to check the menu and send the products on its next round (seconds, not the usual minute).</summary>
    void SendNow();
}

/// <summary>
/// The till's "Online Menu" screen: sections the owner arranges for the public menu, separate from product categories. The sync service
/// reads the same tables when it builds the product list for the cloud (<see cref="MenuCatalog"/>), so a change here reaches the menu
/// on its next round; every change nudges it so that round comes at once.
/// <para>
/// A shop can have thousands of products, so the screen never loads them all: the overview carries counts worked out from plain ids,
/// and a section's picks (with names, prices and pictures) come a page at a time.
/// </para>
/// </summary>
public sealed class OnlineMenuApiService : IOnlineMenuApiService
{
    public const int MaxTake = 50;

    private readonly IOnlineMenuRepository _menu;
    private readonly OnlineSettingsStore _settings;
    private readonly SyncStatus _status;
    private readonly OnlineSignal _signal;

    public OnlineMenuApiService(IOnlineMenuRepository menu, OnlineSettingsStore settings, SyncStatus status, OnlineSignal signal)
    {
        _menu = menu;
        _settings = settings;
        _status = status;
        _signal = signal;
    }

    /// <summary>What it takes to count products without loading them.</summary>
    private sealed record Counts(
        List<OnlineMenuPlacement> Placements,
        Dictionary<int, (int CategoryId, bool Active)> PickedProducts,
        Dictionary<int, int> ActiveByCategory,
        Dictionary<int, int> PickedActiveByCategory,
        int TotalActive)
    {
        /// <summary>Products a pick brings to the menu: 1 or 0 for a product, a category's active products minus those picked on their own.</summary>
        public int Of(int? productId, int? categoryId)
        {
            if (productId is { } pid) return PickedProducts.TryGetValue(pid, out var p) && p.Active ? 1 : 0;
            var cid = categoryId ?? 0;
            return Math.Max(0, ActiveByCategory.GetValueOrDefault(cid) - PickedActiveByCategory.GetValueOrDefault(cid));
        }
    }

    private async Task<Counts> CountAsync()
    {
        var placements = await _menu.GetPlacementsAsync();
        var picked = await _menu.GetProductStatesAsync(placements.Where(p => p.ProductId is not null).Select(p => p.ProductId!.Value).ToList());
        var categories = await _menu.GetCategoriesAsync();
        return new Counts(
            placements,
            picked,
            categories.ToDictionary(c => c.Category.Id, c => c.ActiveProducts),
            picked.Values.Where(p => p.Active).GroupBy(p => p.CategoryId).ToDictionary(g => g.Key, g => g.Count()),
            categories.Sum(c => c.ActiveProducts));
    }

    public async Task<OnlineMenuDto> GetAsync()
    {
        var sections = await _menu.GetSectionsAsync();
        var counts = await CountAsync();
        var dtos = sections.Select(s =>
        {
            var mine = counts.Placements.Where(p => p.SectionId == s.Id).ToList();
            return new OnlineMenuSectionDto { Id = s.Id, Name = s.Name, EntryCount = mine.Count, ProductCount = mine.Sum(p => counts.Of(p.ProductId, p.CategoryId)) };
        }).ToList();
        var shown = sections.Count == 0 ? counts.TotalActive : dtos.Sum(s => s.ProductCount);

        return new OnlineMenuDto
        {
            Sections = dtos,
            ProductsShown = shown,
            ProductsLeftOut = sections.Count == 0 ? 0 : Math.Max(0, counts.TotalActive - shown),
            Status = Status(),
        };
    }

    public async Task<OnlineMenuEntriesDto?> EntriesAsync(int sectionId, int skip, int take)
    {
        if ((await _menu.GetSectionsAsync()).All(s => s.Id != sectionId)) return null;
        var (entries, hasMore) = await _menu.GetEntriesAsync(sectionId, skip, Math.Clamp(take, 1, MaxTake));
        var counts = await CountAsync();
        return new OnlineMenuEntriesDto { Items = entries.Select(e => ToDto(e, counts)).ToList(), HasMore = hasMore };
    }

    private static OnlineMenuEntryDto ToDto(OnlineMenuEntry e, Counts counts)
    {
        if (e.Product is { } p)
        {
            var active = p.IsActive && !p.IsDeleted;
            return new OnlineMenuEntryDto
            {
                Id = e.Id, Kind = "product", ProductId = p.Id, Name = p.Name, Price = p.SellingPrice,
                CurrencyNotation = p.Currency?.Notation ?? "", Image = p.Image, ProductCount = active ? 1 : 0, IsActive = active,
            };
        }

        return new OnlineMenuEntryDto
        {
            Id = e.Id, Kind = "category", CategoryId = e.CategoryId, Name = e.Category?.Name ?? "",
            ProductCount = counts.Of(null, e.CategoryId), IsActive = true,
        };
    }

    private OnlineMenuStatusDto Status()
    {
        var settings = _settings.Current;
        var connected = settings.Enabled && settings.IsConfigured;
        var (linked, url, sent) = _status.Menu();
        var (plan, _) = _status.Plan();
        return new OnlineMenuStatusDto
        {
            Connected = connected,
            // The cloud answers "linked" on the till's regular check-in; until the first one since start-up, nobody knows yet.
            Linked = connected && plan is not null ? linked : null,
            MenuUrl = connected && linked ? url : null,
            EditorUrl = connected ? settings.CloudUrl.TrimEnd('/') + "/owner/menu.html" : null,
            LastSentUtc = connected ? sent : null,
        };
    }

    public async Task<OnlineMenuSectionDto> AddSectionAsync(string name)
    {
        var section = await _menu.AddSectionAsync(name.Trim());
        SendNow();
        return new OnlineMenuSectionDto { Id = section.Id, Name = section.Name, EntryCount = 0, ProductCount = 0 };
    }

    public async Task<bool> RenameSectionAsync(int id, string name) => Nudge(await _menu.RenameSectionAsync(id, name.Trim()));

    public async Task<bool> DeleteSectionAsync(int id) => Nudge(await _menu.DeleteSectionAsync(id));

    public async Task ReorderSectionsAsync(IReadOnlyList<int> ids)
    {
        await _menu.ReorderSectionsAsync(ids);
        SendNow();
    }

    public async Task<bool> PlaceAsync(int sectionId, IReadOnlyCollection<int> productIds, IReadOnlyCollection<int> categoryIds)
        => Nudge(await _menu.PlaceAsync(sectionId, productIds, categoryIds));

    public async Task<bool> RemoveEntryAsync(int entryId) => Nudge(await _menu.RemoveEntryAsync(entryId));

    public async Task<bool> MoveEntryAsync(int entryId, int direction) => Nudge(await _menu.MoveEntryAsync(entryId, direction));

    public async Task<OnlineMenuProductOptionsDto> ProductsAsync(string? term, int skip, int take)
    {
        var (products, hasMore) = await _menu.SearchProductsAsync(term, skip, Math.Clamp(take, 1, MaxTake));
        var names = (await _menu.GetSectionsAsync()).ToDictionary(s => s.Id, s => s.Name);
        var placements = await _menu.GetPlacementsAsync();
        var byProduct = placements.Where(p => p.ProductId is not null).ToDictionary(p => p.ProductId!.Value, p => p.SectionId);
        var byCategory = placements.Where(p => p.CategoryId is not null).ToDictionary(p => p.CategoryId!.Value, p => p.SectionId);

        return new OnlineMenuProductOptionsDto
        {
            HasMore = hasMore,
            Items = products.Select(p =>
            {
                int? own = byProduct.TryGetValue(p.Id, out var s) ? s : null;
                int? via = own is null && byCategory.TryGetValue(p.CategoryId, out var c) ? c : null;
                return new OnlineMenuProductOptionDto
                {
                    Id = p.Id, Name = p.Name, CategoryName = p.Category?.Name, Price = p.SellingPrice,
                    CurrencyNotation = p.Currency?.Notation ?? "", Image = p.Image,
                    SectionId = own,
                    SectionName = own is { } o ? names.GetValueOrDefault(o) : null,
                    ViaCategorySectionName = via is { } v ? names.GetValueOrDefault(v) : null,
                };
            }).ToList(),
        };
    }

    public async Task<IReadOnlyList<OnlineMenuCategoryOptionDto>> CategoriesAsync()
    {
        var categories = await _menu.GetCategoriesAsync();
        var names = (await _menu.GetSectionsAsync()).ToDictionary(s => s.Id, s => s.Name);
        var byCategory = (await _menu.GetPlacementsAsync()).Where(p => p.CategoryId is not null).ToDictionary(p => p.CategoryId!.Value, p => p.SectionId);
        return categories.Select(c =>
        {
            int? at = byCategory.TryGetValue(c.Category.Id, out var s) ? s : null;
            return new OnlineMenuCategoryOptionDto
            {
                Id = c.Category.Id, Name = c.Category.Name, ActiveProducts = c.ActiveProducts,
                SectionId = at, SectionName = at is { } a ? names.GetValueOrDefault(a) : null,
            };
        }).ToList();
    }

    public void SendNow()
    {
        _status.NudgeMenu();
        _signal.Wake();
    }

    private bool Nudge(bool changed)
    {
        if (changed) SendNow();
        return changed;
    }
}
