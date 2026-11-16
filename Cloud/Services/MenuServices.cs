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
}

/// <summary>
/// Folds a product list from the shop PC into its menu. The POS decides the name, price, currency and section; the owner's picture,
/// description and hidden/sold-out marks are kept. A product that left the POS's list leaves the menu; a new one appears in its section,
/// visible. Who orders the menu depends on the list: when the till's online-menu screen arranged it (<see cref="MenuCatalogPush.Arranged"/>),
/// the POS order wins; when it just follows categories, the owner's order in the editor is kept and new items go at the end.
/// </summary>
public sealed class MenuCatalogMerge(ControlDbContext db, MenuImages images, TimeProvider time)
{
    public async Task ApplyAsync(Menu menu, MenuCatalogPush catalog)
    {
        var sections = await db.MenuSections.Where(s => s.MenuId == menu.Id).ToListAsync();
        var items = await db.MenuItems.Where(i => i.MenuId == menu.Id).ToListAsync();

        // Sections: one per POS section (or category) that has products, named as the POS names it.
        var nextSection = sections.Count == 0 ? 0 : sections.Max(s => s.SortOrder) + 1;
        var byKey = new Dictionary<string, MenuSection>(StringComparer.Ordinal);
        foreach (var s in sections.Where(s => s.PosKey is not null)) byKey[s.PosKey!] = s;
        var keysInOrder = catalog.Items.OrderBy(i => i.SectionOrder).Select(i => i.SectionKey).Distinct().ToList();
        foreach (var key in keysInOrder)
        {
            var name = MenuRules.Clean(catalog.Items.First(i => i.SectionKey == key).SectionName, 120) ?? "Other";
            if (byKey.TryGetValue(key, out var section))
                section.Name = name;
            else
            {
                section = new MenuSection { MenuId = menu.Id, Name = name, SortOrder = nextSection++, PosKey = key };
                db.MenuSections.Add(section);
                byKey[key] = section;
            }
        }
        await db.SaveChangesAsync(); // new sections get their ids

        var byProduct = items.Where(i => i.PosProductId is not null).ToDictionary(i => i.PosProductId!.Value);
        var nextItem = items.Count == 0 ? 0 : items.Max(i => i.SortOrder) + 1;
        var seen = new HashSet<int>();
        var linkedInOrder = new List<MenuItem>();
        foreach (var p in catalog.Items.OrderBy(i => i.SectionOrder).ThenBy(i => keysInOrder.IndexOf(i.SectionKey)).ThenBy(i => i.ItemOrder))
        {
            if (!seen.Add(p.ProductId)) continue;
            if (!byProduct.TryGetValue(p.ProductId, out var item))
            {
                item = new MenuItem { MenuId = menu.Id, PosProductId = p.ProductId, SortOrder = nextItem++ };
                db.MenuItems.Add(item);
            }
            item.Name = MenuRules.Clean(p.Name, 200) ?? "-";
            item.PosDescription = MenuRules.Clean(p.Description, 500);
            item.Price = p.Price;
            item.Currency = MenuRules.Clean(p.Currency, 10);
            item.SectionId = byKey[p.SectionKey].Id;
            linkedInOrder.Add(item);
        }

        foreach (var gone in byProduct.Values.Where(i => !seen.Contains(i.PosProductId!.Value)))
        {
            images.Delete(gone.ImageFile);
            db.MenuItems.Remove(gone);
        }

        // A POS section with nothing left in it (and nothing added by hand) goes too.
        var stillUsed = keysInOrder.ToHashSet(StringComparer.Ordinal);
        var handSections = items.Where(i => i.PosProductId is null && i.SectionId is not null).Select(i => i.SectionId!.Value).ToHashSet();
        var removed = byKey.Values.Where(s => !stillUsed.Contains(s.PosKey!) && !handSections.Contains(s.Id)).ToList();
        db.MenuSections.RemoveRange(removed);

        if (catalog.Arranged)
        {
            // The POS order first, then whatever the owner added by hand, in the order they had.
            var order = 0;
            foreach (var key in keysInOrder) byKey[key].SortOrder = order++;
            foreach (var s in sections.Where(s => (s.PosKey is null || !stillUsed.Contains(s.PosKey)) && !removed.Contains(s)).OrderBy(s => s.SortOrder))
                s.SortOrder = order++;
            order = 0;
            foreach (var item in linkedInOrder) item.SortOrder = order++;
            foreach (var item in items.Where(i => i.PosProductId is null).OrderBy(i => i.SortOrder)) item.SortOrder = order++;
        }

        menu.PosArranged = catalog.Arranged;
        menu.CatalogHash = catalog.Hash;
        menu.CatalogAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The menu stops following the POS: its POS items and sections stay, as ordinary hand-made ones the owner can now edit freely.
    /// Used when the owner unlinks, links another branch, or the branch is removed.
    /// </summary>
    public async Task DetachAsync(Menu menu)
    {
        await db.MenuItems.Where(i => i.MenuId == menu.Id && i.PosProductId != null).ExecuteUpdateAsync(u => u
            .SetProperty(i => i.PosProductId, (int?)null)
            .SetProperty(i => i.Description, i => i.Description ?? i.PosDescription)
            .SetProperty(i => i.PosDescription, (string?)null));
        await db.MenuSections.Where(s => s.MenuId == menu.Id && s.PosKey != null).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.PosKey, (string?)null));
        menu.LinkedSiteId = null;
        menu.CatalogHash = null;
        menu.CatalogAt = null;
        menu.PosArranged = false;
        await db.SaveChangesAsync();
    }
}
