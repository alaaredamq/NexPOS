using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cloud.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sync;

namespace Cloud.Services;

/// <summary>
/// Pictures on online menus: one file each under DataRoot/menu-images, named by random letters, so a name cannot be guessed and a
/// replaced picture gets a new name (browsers may cache a name forever). The editor shrinks pictures before uploading; this only checks
/// that a file really is a JPEG, PNG or WebP and not too large.
/// </summary>
public sealed partial class MenuImages(IOptions<CloudOptions> options)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private readonly string _root = Path.Combine(Path.GetFullPath(options.Value.DataRoot), "menu-images");

    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp)$")]
    private static partial Regex FileName();

    public static bool IsValidName(string? name) => name is not null && FileName().IsMatch(name);

    /// <summary>The image kind from its first bytes, or null when it is none of the three.</summary>
    public static string? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "jpg";
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return "webp";
        return null;
    }

    public async Task<string> SaveAsync(byte[] data, string ext)
    {
        Directory.CreateDirectory(_root);
        var name = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + "." + ext;
        await File.WriteAllBytesAsync(Path.Combine(_root, name), data);
        return name;
    }

    public string? PathOf(string name) => IsValidName(name) ? Path.Combine(_root, name) : null;

    public void Delete(string? name)
    {
        if (PathOf(name ?? "") is { } path)
            try { File.Delete(path); } catch (IOException) { /* a leftover file is harmless; it is unreachable without its name */ }
    }

    public static string ContentType(string name) => Path.GetExtension(name) switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };
}

/// <summary>Small rules shared by the editor API and the public page.</summary>
public static partial class MenuRules
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,58}[a-z0-9])$")]
    private static partial Regex SlugPattern();

    public static bool IsValidSlug(string? slug) => slug is not null && SlugPattern().IsMatch(slug) && !slug.Contains("--");

    /// <summary>"Abu Ali's Grill" becomes "abu-alis-grill"; a name with no Latin letters gets a short random address instead.</summary>
    public static string SlugFrom(string? name)
    {
        var sb = new StringBuilder();
        foreach (var ch in (name ?? "").Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (ch is ' ' or '-' or '_' or '.' or '&' && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 50) slug = slug[..50].Trim('-');
        return IsValidSlug(slug) ? slug : "menu-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
    }

    /// <summary>A free address based on <paramref name="wanted"/>: itself, or with -2, -3... added.</summary>
    public static async Task<string> FreeSlugAsync(ControlDbContext db, string wanted, int? exceptMenuId = null)
    {
        for (var n = 1; n < 1000; n++)
        {
            var candidate = n == 1 ? wanted : $"{wanted[..Math.Min(wanted.Length, 54)].TrimEnd('-')}-{n}";
            if (!await db.Menus.AnyAsync(m => m.Slug == candidate && m.Id != exceptMenuId)) return candidate;
        }
        return "menu-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
    }

    /// <summary>"$12.50", "$8", "450,000 LBP". Whole amounts lose their decimals; others keep two.</summary>
    public static string Price(decimal price, string? currency)
    {
        var amount = price == decimal.Truncate(price)
            ? price.ToString("#,0", CultureInfo.InvariantCulture)
            : price.ToString("#,0.00", CultureInfo.InvariantCulture);
        var c = (currency ?? "").Trim();
        if (c.Length == 0) return amount;
        return c is "$" or "€" or "£" ? c + amount : amount + " " + c;
    }

    public static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    /// <summary>
    /// What the public page shows at a branch: visible dishes in visible sections that the branch sells, plus the hand-made ones (sold
    /// everywhere). <paramref name="site"/> null = no branch is linked, so everything visible.
    /// </summary>
    public static IQueryable<MenuItem> Visible(ControlDbContext db, int menuId, Guid? site)
    {
        var hiddenSections = db.MenuSections.Where(s => s.MenuId == menuId && s.IsHidden).Select(s => (int?)s.Id);
        var items = db.MenuItems.Where(i => i.MenuId == menuId && !i.IsHidden && (i.SectionId == null || !hiddenSections.Contains(i.SectionId)));
        return site is { } at ? items.Where(i => !i.Branches.Any() || i.Branches.Any(r => r.SiteId == at)) : items;
    }

    /// <summary>
    /// A dish's price at a branch for an order type: the branch's POS price (or the dish's own for a hand-made one), then the type's
    /// rule unless the dish has its own price for that type. Expects the dish's <see cref="MenuItem.Branches"/> and
    /// <see cref="MenuItem.TypePrices"/> loaded (at least the ones for this branch and type).
    /// </summary>
    public static (decimal Price, string Currency) PriceAt(MenuItem item, Guid? site, MenuOrderType? type, string menuCurrency)
    {
        var row = site is { } at ? item.Branches.FirstOrDefault(r => r.SiteId == at) : null;
        var normal = row?.Price ?? item.Price;
        var currency = (row is null ? item.Currency : row.Currency) ?? menuCurrency;
        var own = type is null ? null : item.TypePrices.FirstOrDefault(p => p.OrderTypeId == type.Id)?.Price;
        return (MenuPriceAdjust.Apply(normal, type, own), currency);
    }

    /// <summary>A branch's word in the menu's address (?b=), from its name and free within the menu: "Hamra" becomes "hamra".</summary>
    public static string BranchSlug(string? name, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.Ordinal);
        var stem = SlugFrom(name);
        if (stem.StartsWith("menu-", StringComparison.Ordinal)) stem = "branch";
        if (stem.Length > 34) stem = stem[..34].Trim('-');
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? stem : $"{stem}-{n}";
            if (!used.Contains(candidate)) return candidate;
        }
    }
}

/// <summary>
/// Folds a product list from one branch's shop PC into the shop's menu. The POS decides the name, price, currency and section; the owner's
/// picture, description and hidden/sold-out marks are kept. A product that left the POS's list leaves that branch (and the menu, when no
/// branch sells it any more); a new one appears in its section, visible.
/// <para>
/// Several branches share one menu. Each branch numbers its products and categories its own way, so across branches a dish and a section
/// are known by their name: "Labneh 500g" at two branches is one dish with two prices. Within a branch the product's POS id keeps the
/// link when it is renamed. The main branch (the first) decides where a dish sits and, when its till arranges the menu
/// (<see cref="MenuCatalogPush.Arranged"/>), the order; the others only add what it lacks. Without arranging, the owner's order in the
/// editor is kept and new items go at the end.
/// </para>
/// </summary>
public sealed class MenuCatalogMerge(ControlDbContext db, MenuImages images, TimeProvider time)
{
    // One merge at a time per menu: two pushes that cross (a retry, two branches at once) would each add the same new sections and items.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Gates = new();

    /// <summary>How two names are compared when telling whether two sections or dishes are the same one.</summary>
    public static string Same(string? name) => (name ?? "").Trim().ToUpperInvariant();

    public async Task ApplyAsync(Menu menu, MenuBranch branch, MenuCatalogPush catalog)
    {
        var gate = Gates.GetOrAdd(menu.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { await MergeAsync(menu, branch, catalog); }
        finally { gate.Release(); }
    }

    private async Task MergeAsync(Menu menu, MenuBranch branch, MenuCatalogPush catalog)
    {
        var site = branch.SiteId;
        var sections = await db.MenuSections.Where(s => s.MenuId == menu.Id).ToListAsync();
        var items = await db.MenuItems.Where(i => i.MenuId == menu.Id).Include(i => i.Branches).Include(i => i.TypePrices).ToListAsync();
        var main = await db.MenuBranches.Where(b => b.MenuId == menu.Id).OrderBy(b => b.SortOrder).ThenBy(b => b.Id).Select(b => b.SiteId).FirstAsync();
        var isMain = main == site;
        MendDoubles(sections, items);

        // Sections: one per POS section (or category) that has products, named as the POS names it.
        var nextSection = sections.Count == 0 ? 0 : sections.Max(s => s.SortOrder) + 1;
        var byName = sections.GroupBy(s => Same(s.Name)).ToDictionary(g => g.Key, g => g.OrderBy(s => s.PosKey is null).ThenBy(s => s.Id).First());
        var keysInOrder = catalog.Items.OrderBy(i => i.SectionOrder).Select(i => i.SectionKey).Distinct().ToList();
        var nameOfKey = keysInOrder.ToDictionary(k => k, k => MenuRules.Clean(catalog.Items.First(i => i.SectionKey == k).SectionName, 120) ?? "Other", StringComparer.Ordinal);
        var namesNow = nameOfKey.Values.Select(Same).ToHashSet();
        var sectionOf = new Dictionary<string, MenuSection>(StringComparer.Ordinal);
        foreach (var key in keysInOrder)
        {
            var name = nameOfKey[key];
            var section = byName.GetValueOrDefault(Same(name));
            // Renamed at this till: the section it made under this key, whose old name the till no longer sends, takes the new name
            // (keeping its place and whether it is hidden).
            if (section is null && sections.FirstOrDefault(s => s.PosKey == key && !namesNow.Contains(Same(s.Name))) is { } renamed)
            {
                renamed.Name = name;
                section = renamed;
            }
            if (section is null)
            {
                section = new MenuSection { MenuId = menu.Id, Name = name, SortOrder = nextSection++, PosKey = key };
                db.MenuSections.Add(section);
                sections.Add(section);
            }
            // A section of that name made by hand becomes the POS one, so the menu does not show the name twice.
            section.PosKey ??= key;
            if (isMain) section.Name = name;
            byName[Same(name)] = section;
            sectionOf[key] = section;
        }
        await db.SaveChangesAsync(); // new sections get their ids

        // Dishes.
        var here = items.SelectMany(i => i.Branches.Where(r => r.SiteId == site).Select(r => (Item: i, Row: r)))
            .GroupBy(x => x.Row.PosProductId).ToDictionary(g => g.Key, g => g.First());
        var dishByName = items.GroupBy(i => Same(i.Name)).ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.Branches.Count > 0).ThenBy(i => i.Id).First());
        var nextItem = items.Count == 0 ? 0 : items.Max(i => i.SortOrder) + 1;
        var seen = new HashSet<int>();
        var filled = new HashSet<MenuItem>(); // dishes this list already gave a product to
        var inOrder = new List<MenuItem>();
        MenuItem NewDish(string name)
        {
            var dish = new MenuItem { MenuId = menu.Id, Name = name, SortOrder = nextItem++ };
            db.MenuItems.Add(dish);
            items.Add(dish);
            dishByName[Same(name)] = dish;
            return dish;
        }
        MenuItemBranch Attach(MenuItem dish, int productId)
        {
            var row = new MenuItemBranch { SiteId = site, PosProductId = productId, MenuItem = dish };
            dish.Branches.Add(row);
            return row;
        }

        foreach (var p in catalog.Items.OrderBy(i => i.SectionOrder).ThenBy(i => keysInOrder.IndexOf(i.SectionKey)).ThenBy(i => i.ItemOrder))
        {
            if (!seen.Add(p.ProductId)) continue;
            var name = MenuRules.Clean(p.Name, 200) ?? "-";
            MenuItem dish;
            MenuItemBranch row;
            if (here.TryGetValue(p.ProductId, out var known))
            {
                (dish, row) = known;
                if (Same(dish.Name) != Same(name))
                {
                    // Renamed at this till. A dish only this branch sells takes the new name (and keeps its picture); a shared one stays,
                    // and this branch's product becomes the dish of its new name.
                    var other = dishByName.GetValueOrDefault(Same(name));
                    if (dish.Branches.Count == 1 && other is null)
                    {
                        dishByName.Remove(Same(dish.Name));
                        dish.Name = name;
                        dishByName[Same(name)] = dish;
                    }
                    else
                    {
                        dish.Branches.Remove(row);
                        db.MenuItemBranches.Remove(row);
                        dish = other ?? NewDish(name);
                        if (filled.Contains(dish) || dish.Branches.Any(r => r.SiteId == site)) continue;
                        row = Attach(dish, p.ProductId);
                    }
                }
            }
            else
            {
                dish = dishByName.GetValueOrDefault(Same(name)) ?? NewDish(name);
                // Two products of one name at one branch: the menu shows the dish once, at the first one's price.
                if (filled.Contains(dish) || dish.Branches.Any(r => r.SiteId == site)) continue;
                row = Attach(dish, p.ProductId);
            }
            filled.Add(dish);
            row.Price = p.Price;
            row.Currency = MenuRules.Clean(p.Currency, 10);

            // The main branch decides the dish's name, normal price and place; another branch only for a dish the main one does not sell.
            if (isMain || !dish.Branches.Any(r => r.SiteId == main))
            {
                dish.Name = name;
                dish.Price = p.Price;
                dish.Currency = row.Currency;
                dish.PosDescription = MenuRules.Clean(p.Description, 500);
                dish.SectionId = sectionOf[p.SectionKey].Id;
            }
            else dish.SectionId ??= sectionOf[p.SectionKey].Id;
            inOrder.Add(dish);
        }

        // Products this branch no longer sells. A dish no branch sells any more leaves the menu.
        foreach (var (dish, row) in here.Values.Where(x => !seen.Contains(x.Row.PosProductId)))
        {
            dish.Branches.Remove(row);
            db.MenuItemBranches.Remove(row);
            if (dish.Branches.Count == 0)
            {
                images.Delete(dish.ImageFile);
                db.MenuItems.Remove(dish);
                items.Remove(dish);
            }
        }

        MendDoubles(sections, items);

        // A POS section with nothing left in it goes too (one holding dishes added by hand stays).
        foreach (var empty in sections.Where(s => s.PosKey is not null && !items.Any(i => i.SectionId == s.Id)).ToList())
        {
            db.MenuSections.Remove(empty);
            sections.Remove(empty);
        }

        if (catalog.Arranged && isMain)
        {
            // The main till's order first, then whatever else is on the menu, in the order it had.
            var order = 0;
            var arranged = keysInOrder.Select(k => sectionOf[k]).Where(sections.Contains).Distinct().ToList();
            foreach (var s in arranged) s.SortOrder = order++;
            foreach (var s in sections.Except(arranged).OrderBy(s => s.SortOrder)) s.SortOrder = order++;
            order = 0;
            var listed = inOrder.Where(items.Contains).Distinct().ToList();
            foreach (var i in listed) i.SortOrder = order++;
            foreach (var i in items.Except(listed).OrderBy(i => i.SortOrder)) i.SortOrder = order++;
        }

        branch.PosArranged = catalog.Arranged;
        branch.CatalogHash = catalog.Hash;
        branch.CatalogAt = time.GetUtcNow().UtcDateTime;
        menu.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A branch stops filling the menu (the owner took it off, or the branch was removed). Its prices go; a dish only it sold stays, as an
    /// ordinary hand-made one the owner can now edit, and so does a section left with no POS dish. The other branches send their lists
    /// again, so a new main branch takes over the names and places.
    /// </summary>
    public async Task DetachAsync(Menu menu, Guid siteId)
    {
        var branch = await db.MenuBranches.FirstOrDefaultAsync(b => b.MenuId == menu.Id && b.SiteId == siteId);
        if (branch is null) return;
        await db.MenuItemBranches.Where(r => r.SiteId == siteId && r.MenuItem.MenuId == menu.Id).ExecuteDeleteAsync();
        db.MenuBranches.Remove(branch);
        await db.MenuBranches.Where(b => b.MenuId == menu.Id && b.SiteId != siteId).ExecuteUpdateAsync(u => u.SetProperty(b => b.CatalogHash, (string?)null));
        await db.MenuItems.Where(i => i.MenuId == menu.Id && !i.Branches.Any()).ExecuteUpdateAsync(u => u
            .SetProperty(i => i.Description, i => i.Description ?? i.PosDescription)
            .SetProperty(i => i.PosDescription, (string?)null));
        await db.MenuSections.Where(s => s.MenuId == menu.Id && s.PosKey != null
                && !db.MenuItems.Any(i => i.SectionId == s.Id && i.Branches.Any()))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.PosKey, (string?)null));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Once a POS is involved, one section per name and one dish per name: copies (made by hand before the POS sent the same, left by
    /// unlinking a branch, or by two pushes that crossed before merges were taken one at a time) are folded into one. The dish kept is
    /// a POS one, preferably with the owner's picture or words; the copies' branch prices, pictures and words move to it.
    /// </summary>
    private void MendDoubles(List<MenuSection> sections, List<MenuItem> items)
    {
        foreach (var twins in sections.GroupBy(s => Same(s.Name)).Where(g => g.Count() > 1 && g.Any(s => s.PosKey is not null)).ToList())
        {
            var keep = twins.OrderBy(s => s.PosKey is null).ThenBy(s => s.Id).First();
            foreach (var extra in twins.Where(s => s != keep))
            {
                foreach (var i in items.Where(i => i.SectionId == extra.Id)) i.SectionId = keep.Id;
                db.MenuSections.Remove(extra);
                sections.Remove(extra);
            }
        }
        foreach (var twins in items.GroupBy(i => Same(i.Name)).Where(g => g.Count() > 1 && g.Any(i => i.Branches.Count > 0)).ToList())
        {
            var keep = twins.OrderByDescending(i => i.Branches.Count > 0).ThenByDescending(i => i.ImageFile is not null)
                .ThenByDescending(i => i.Description is not null).ThenBy(i => i.Id).First();
            foreach (var extra in twins.Where(i => i != keep))
            {
                foreach (var row in extra.Branches.ToList())
                    if (keep.Branches.All(r => r.SiteId != row.SiteId)) { extra.Branches.Remove(row); row.MenuItem = keep; keep.Branches.Add(row); }
                foreach (var tp in extra.TypePrices.ToList())
                    if (keep.TypePrices.All(x => x.OrderTypeId != tp.OrderTypeId)) { extra.TypePrices.Remove(tp); tp.MenuItem = keep; keep.TypePrices.Add(tp); }
                Fold(keep, extra);
                items.Remove(extra);
            }
        }
    }

    /// <summary>Removes <paramref name="copy"/>, keeping its picture and description on <paramref name="keep"/> when that has none.</summary>
    private void Fold(MenuItem keep, MenuItem copy)
    {
        if (keep.ImageFile is null) keep.ImageFile = copy.ImageFile;
        else if (copy.ImageFile is not null && copy.ImageFile != keep.ImageFile) images.Delete(copy.ImageFile);
        keep.Description ??= copy.Description;
        db.MenuItems.Remove(copy);
    }
}
