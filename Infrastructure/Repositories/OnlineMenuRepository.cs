using Core.Entities.Products;
using Core.IRepositories;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class OnlineMenuRepository(ErpDbContext db) : IOnlineMenuRepository
{
    public Task<List<OnlineMenuSection>> GetSectionsAsync()
        => db.OnlineMenuSections.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();

    public Task<List<OnlineMenuPlacement>> GetPlacementsAsync()
        => db.OnlineMenuEntries.AsNoTracking().Select(e => new OnlineMenuPlacement(e.Id, e.SectionId, e.ProductId, e.CategoryId)).ToListAsync();

    public async Task<(List<OnlineMenuEntry> Items, bool HasMore)> GetEntriesAsync(int sectionId, int skip, int take)
    {
        var rows = await db.OnlineMenuEntries.AsNoTracking().Where(e => e.SectionId == sectionId)
            .Include(e => e.Product!).ThenInclude(p => p.Currency)
            .Include(e => e.Category)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Id)
            .Skip(Math.Max(0, skip)).Take(take + 1)
            .ToListAsync();
        return (rows.Take(take).ToList(), rows.Count > take);
    }

    public async Task<Dictionary<int, (int CategoryId, bool Active)>> GetProductStatesAsync(IReadOnlyCollection<int> productIds)
    {
        if (productIds.Count == 0) return [];
        var rows = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.CategoryId, Active = p.IsActive && !p.IsDeleted }).ToListAsync();
        return rows.ToDictionary(r => r.Id, r => (r.CategoryId, r.Active));
    }

    public async Task<bool> MoveEntryAsync(int entryId, int direction)
    {
        var entry = await db.OnlineMenuEntries.FirstOrDefaultAsync(e => e.Id == entryId);
        if (entry is null) return false;
        var siblings = await db.OnlineMenuEntries.Where(e => e.SectionId == entry.SectionId).OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToListAsync();
        var at = siblings.FindIndex(e => e.Id == entryId);
        var to = at + Math.Sign(direction);
        if (direction == 0 || to < 0 || to >= siblings.Count) return true;
        (siblings[at], siblings[to]) = (siblings[to], siblings[at]);
        // Renumber the section so ties left by older rows can never make a move do nothing.
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].SortOrder == i) continue;
            siblings[i].SortOrder = i;
            siblings[i].UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<OnlineMenuSection> AddSectionAsync(string name)
    {
        var last = await db.OnlineMenuSections.Select(s => (int?)s.SortOrder).MaxAsync() ?? -1;
        var section = new OnlineMenuSection { Name = name, SortOrder = last + 1, CreatedAt = DateTime.UtcNow };
        db.OnlineMenuSections.Add(section);
        await db.SaveChangesAsync();
        return section;
    }

    public async Task<bool> RenameSectionAsync(int id, string name)
    {
        var section = await db.OnlineMenuSections.FirstOrDefaultAsync(s => s.Id == id);
        if (section is null) return false;
        section.Name = name;
        section.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteSectionAsync(int id)
    {
        var section = await db.OnlineMenuSections.Include(s => s.Entries).FirstOrDefaultAsync(s => s.Id == id);
        if (section is null) return false;
        db.OnlineMenuEntries.RemoveRange(section.Entries);
        db.OnlineMenuSections.Remove(section);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task ReorderSectionsAsync(IReadOnlyList<int> ids)
    {
        var sections = await db.OnlineMenuSections.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        var ordered = ids.Distinct().Select(id => sections.FirstOrDefault(s => s.Id == id)).OfType<OnlineMenuSection>()
            .Concat(sections.Where(s => !ids.Contains(s.Id))).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].SortOrder == i) continue;
            ordered[i].SortOrder = i;
            ordered[i].UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> PlaceAsync(int sectionId, IReadOnlyCollection<int> productIds, IReadOnlyCollection<int> categoryIds)
    {
        if (!await db.OnlineMenuSections.AnyAsync(s => s.Id == sectionId)) return false;
        var products = await db.Products.Where(p => productIds.Contains(p.Id) && !p.IsDeleted).Select(p => p.Id).ToListAsync();
        var categories = await db.ProductCategories.Where(c => categoryIds.Contains(c.Id)).Select(c => c.Id).ToListAsync();

        // Each product or category lives in one place on the menu: picking it here takes it out of where it was.
        var existing = await db.OnlineMenuEntries
            .Where(e => (e.ProductId != null && products.Contains(e.ProductId.Value)) || (e.CategoryId != null && categories.Contains(e.CategoryId.Value)))
            .ToListAsync();
        db.OnlineMenuEntries.RemoveRange(existing.Where(e => e.SectionId != sectionId));
        var already = existing.Where(e => e.SectionId == sectionId).ToList();
        await db.SaveChangesAsync();

        var next = (await db.OnlineMenuEntries.Where(e => e.SectionId == sectionId).Select(e => (int?)e.SortOrder).MaxAsync() ?? -1) + 1;
        var now = DateTime.UtcNow;
        foreach (var id in categories.Where(id => already.All(e => e.CategoryId != id)))
            db.OnlineMenuEntries.Add(new OnlineMenuEntry { SectionId = sectionId, CategoryId = id, SortOrder = next++, CreatedAt = now });
        foreach (var id in products.Where(id => already.All(e => e.ProductId != id)))
            db.OnlineMenuEntries.Add(new OnlineMenuEntry { SectionId = sectionId, ProductId = id, SortOrder = next++, CreatedAt = now });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveEntryAsync(int entryId)
    {
        var entry = await db.OnlineMenuEntries.FirstOrDefaultAsync(e => e.Id == entryId);
        if (entry is null) return false;
        db.OnlineMenuEntries.Remove(entry);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<(List<Product> Items, bool HasMore)> SearchProductsAsync(string? term, int skip, int take)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive && !p.IsDeleted);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            query = query.Where(p => EF.Functions.Like(p.Name, $"%{t}%") || EF.Functions.Like(p.Barcode, $"%{t}%"));
        }
        var rows = await query.Include(p => p.Category).Include(p => p.Currency)
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Skip(Math.Max(0, skip)).Take(take + 1).ToListAsync();
        return (rows.Take(take).ToList(), rows.Count > take);
    }

    public async Task<List<(ProductCategory Category, int ActiveProducts)>> GetCategoriesAsync()
    {
        var counts = await db.Products.Where(p => p.IsActive && !p.IsDeleted)
            .GroupBy(p => p.CategoryId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var categories = await db.ProductCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        return categories.Select(c => (c, counts.GetValueOrDefault(c.Id))).ToList();
    }
}
