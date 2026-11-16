using Cloud.Data;
using Cloud.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Pages.Public;

/// <summary>
/// A shop's online menu, for anyone: /m/{slug}. An unpublished menu answers 404 unless its preview key is given.
/// <para>
/// A menu can hold thousands of items, so the page carries only the first <see cref="FirstPaint"/> items; every section still gets its
/// heading (so the section chips work) and a placeholder sized for what is left, which menu-page.js fills from <c>/m/{slug}/items</c> as
/// it scrolls into view. Search asks the server too, so it finds items that are not loaded yet.
/// </para>
/// </summary>
public class MenuModel(ControlDbContext db) : PageModel
{
    public const int FirstPaint = 60;

    public sealed record Group(MenuSection? Section, List<MenuItem> Items, int Total)
    {
        public int Key => Section?.Id ?? 0;
        public int Remaining => Total - Items.Count;
    }

    public Menu Menu { get; private set; } = null!;
    public List<Group> Groups { get; } = [];
    public bool IsPreview { get; private set; }
    public string Origin { get; private set; } = "";
    public string? PreviewKey { get; private set; }
    public bool Arabic => Menu.Language == "ar";

    /// <summary>The shop's chosen listing style (a class on the page; menu.css draws each).</summary>
    public string Style => MenuListingStyles.IsValid(Menu.ListingStyle) ? Menu.ListingStyle : MenuListingStyles.Default;

    /// <summary>The letter shown where a dish has no picture.</summary>
    public static string Initial(string name) => string.IsNullOrWhiteSpace(name) ? "" : name.Trim()[..1].ToUpperInvariant();

    public async Task<IActionResult> OnGetAsync(string slug, string? preview)
    {
        var menu = await db.Menus.AsNoTracking().Include(m => m.Client).FirstOrDefaultAsync(m => m.Slug == slug.ToLower());
        if (menu is null || !menu.Client.IsActive) return NotFound();
        IsPreview = !menu.IsPublished;
        if (IsPreview && (string.IsNullOrEmpty(preview) || preview != menu.PreviewKey)) return NotFound();
        Menu = menu;
        PreviewKey = IsPreview ? preview : null;
        Origin = PublicSite.OriginOf(HttpContext);

        var sections = await db.MenuSections.AsNoTracking().Where(s => s.MenuId == menu.Id).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        var visible = sections.Where(s => !s.IsHidden).ToList();
        var allIds = sections.Select(s => s.Id).ToHashSet();

        // How many visible items each place holds (counts only; no rows loaded).
        var counts = await db.MenuItems.Where(i => i.MenuId == menu.Id && !i.IsHidden).GroupBy(i => i.SectionId)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var looseTotal = counts.Where(c => c.Key is null || !allIds.Contains(c.Key.Value)).Sum(c => c.Count);

        var budget = FirstPaint;
        foreach (var s in visible)
        {
            var total = counts.FirstOrDefault(c => c.Key == s.Id)?.Count ?? 0;
            if (total == 0) continue;
            var items = budget > 0 ? await ItemsAsync(menu.Id, s.Id, budget) : [];
            budget -= items.Count;
            Groups.Add(new Group(s, items, total));
        }
        if (looseTotal > 0)
        {
            var items = budget > 0 ? await ItemsAsync(menu.Id, null, budget) : [];
            Groups.Add(new Group(null, items, looseTotal));
        }

        if (IsPreview) Response.Headers["X-Robots-Tag"] = "noindex";
        Response.Headers.CacheControl = IsPreview ? "no-store" : "public, max-age=60";
        return Page();

        Task<List<MenuItem>> ItemsAsync(int menuId, int? sectionId, int take)
        {
            var q = db.MenuItems.AsNoTracking().Where(i => i.MenuId == menuId && !i.IsHidden);
            q = sectionId is { } sid ? q.Where(i => i.SectionId == sid) : q.Where(i => i.SectionId == null || !allIds.Contains(i.SectionId.Value));
            return q.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Take(take).ToListAsync();
        }
    }

    public string PriceOf(MenuItem i) => MenuRules.Price(i.Price, i.Currency ?? Menu.Currency);

    public string T(string en, string ar) => Arabic ? ar : en;

    public string? ShareImage =>
        Menu.LogoFile is not null ? $"{Origin}/m/img/{Menu.LogoFile}"
        : Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.ImageFile is not null) is { } first ? $"{Origin}/m/img/{first.ImageFile}"
        : null;

    public string Description =>
        Menu.Tagline ?? T($"{Menu.Title}: the menu, with prices.", $"{Menu.Title}: القائمة والأسعار.");
}
