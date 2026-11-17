using Cloud.Data;
using Cloud.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Pages.Public;

/// <summary>
/// A shop's online menu, for anyone: /m/{slug}. An unpublished menu answers 404 unless its preview key is given.
/// <para>
/// A shop with several branches shares one menu: <c>?b=</c> picks the branch (a table's QR carries it), and without it the page asks which
/// branch the guest is at. The dishes are shared; the prices, and which dishes show, are that branch's. When the owner made order types
/// (dine in, take out, delivery...), <c>?t=</c> picks one and every price follows it.
/// </para>
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

    public sealed record BranchChoice(string Slug, string Name);

    public Menu Menu { get; private set; } = null!;
    public List<Group> Groups { get; } = [];
    public bool IsPreview { get; private set; }
    public string Origin { get; private set; } = "";
    public string? PreviewKey { get; private set; }
    public bool Arabic => Menu.Language == "ar";

    /// <summary>The branches the guest can pick from (only when there are two or more).</summary>
    public List<BranchChoice> Branches { get; } = [];
    /// <summary>The branch whose prices the page shows; null when the menu has no branch, or the guest still has to pick one.</summary>
    public BranchChoice? Branch { get; private set; }
    private Guid? _site;
    /// <summary>Several branches and none picked: the page shows the branch list instead of the dishes.</summary>
    public bool NeedsBranch => Branches.Count > 1 && Branch is null;

    public List<MenuOrderType> OrderTypes { get; private set; } = [];
    public MenuOrderType? OrderType { get; private set; }

    /// <summary>The shop's chosen listing style (a class on the page; menu.css draws each).</summary>
    public string Style => MenuListingStyles.IsValid(Menu.ListingStyle) ? Menu.ListingStyle : MenuListingStyles.Default;

    /// <summary>The letter shown where a dish has no picture.</summary>
    public static string Initial(string name) => string.IsNullOrWhiteSpace(name) ? "" : name.Trim()[..1].ToUpperInvariant();

    public async Task<IActionResult> OnGetAsync(string slug, string? preview, string? b, int? t)
    {
        var menu = await db.Menus.AsNoTracking().Include(m => m.Client).FirstOrDefaultAsync(m => m.Slug == slug.ToLower());
        if (menu is null || !menu.Client.IsActive) return NotFound();
        IsPreview = !menu.IsPublished;
        if (IsPreview && (string.IsNullOrEmpty(preview) || preview != menu.PreviewKey)) return NotFound();
        Menu = menu;
        PreviewKey = IsPreview ? preview : null;
        Origin = PublicSite.OriginOf(HttpContext);
        if (IsPreview) Response.Headers["X-Robots-Tag"] = "noindex";
        Response.Headers.CacheControl = IsPreview ? "no-store" : "public, max-age=60";

        var context = await MenuContext.LoadAsync(db, menu, b, t);
        Branches.AddRange(context.Branches.Count > 1 ? context.Branches.Select(x => new BranchChoice(x.Slug, x.Name)) : []);
        if (context.Branch is { } chosen) { Branch = new BranchChoice(chosen.Slug, chosen.Name); _site = chosen.SiteId; }
        OrderTypes = context.Types;
        OrderType = context.Type;
        if (NeedsBranch) return Page();

        var sections = await db.MenuSections.AsNoTracking().Where(s => s.MenuId == menu.Id).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        var visible = sections.Where(s => !s.IsHidden).ToList();
        var allIds = sections.Select(s => s.Id).ToHashSet();

        // How many dishes each place holds at this branch (counts only; no rows loaded).
        var counts = await MenuRules.Visible(db, menu.Id, _site).GroupBy(i => i.SectionId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var looseTotal = counts.Where(c => c.Key is null || !allIds.Contains(c.Key.Value)).Sum(c => c.Count);

        var budget = FirstPaint;
        foreach (var s in visible)
        {
            var total = counts.FirstOrDefault(c => c.Key == s.Id)?.Count ?? 0;
            if (total == 0) continue;
            var items = budget > 0 ? await ItemsAsync(s.Id, budget) : [];
            budget -= items.Count;
            Groups.Add(new Group(s, items, total));
        }
        if (looseTotal > 0)
        {
            var items = budget > 0 ? await ItemsAsync(null, budget) : [];
            Groups.Add(new Group(null, items, looseTotal));
        }
        return Page();

        Task<List<MenuItem>> ItemsAsync(int? sectionId, int take)
        {
            var q = MenuRules.Visible(db, menu.Id, _site).AsNoTracking();
            q = sectionId is { } sid ? q.Where(i => i.SectionId == sid) : q.Where(i => i.SectionId == null || !allIds.Contains(i.SectionId.Value));
            return MenuContext.WithPrices(q.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Take(take), _site, OrderType).ToListAsync();
        }
    }

    private (decimal Price, string Currency) PriceAt(MenuItem i) => MenuRules.PriceAt(i, _site, OrderType, Menu.Currency);
    public string PriceOf(MenuItem i) { var (p, c) = PriceAt(i); return MenuRules.Price(p, c); }
    /// <summary>The plain number and currency, for the order the guest builds on the page (menu-page.js adds them up).</summary>
    public string Amount(MenuItem i) => PriceAt(i).Price.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string CurrencyOf(MenuItem i) => PriceAt(i).Currency;

    /// <summary>This page's address with the branch and order type changed (and the preview key kept).</summary>
    public string Link(string? branch, int? type)
    {
        var q = new List<string>();
        if (PreviewKey is not null) q.Add("preview=" + Uri.EscapeDataString(PreviewKey));
        if (branch is not null) q.Add("b=" + Uri.EscapeDataString(branch));
        if (type is not null) q.Add("t=" + type);
        return "/m/" + Menu.Slug + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    public string T(string en, string ar) => Arabic ? ar : en;

    /// <summary>The words menu-page.js needs for what it draws itself (the dish sheet, the order), in the page's language.</summary>
    public Dictionary<string, string> Words => new()
    {
        ["soldOut"] = T("Sold out", "نفد"),
        ["error"] = T("Could not load more. Tap to try again.", "تعذّر تحميل المزيد. اضغط للمحاولة مجدداً."),
        ["addName"] = T("Add", "أضف"),
        ["add"] = T("Add to order", "أضف إلى الطلب"),
        ["update"] = T("Update order", "حدّث الطلب"),
        ["remove"] = T("Remove from order", "احذف من الطلب"),
        ["added"] = T("Added to your order", "أُضيف إلى طلبك"),
        ["view"] = T("View order", "عرض الطلب"),
        ["total"] = T("Total", "المجموع"),
        ["copied"] = T("Copied", "تم النسخ"),
        ["clearSure"] = T("Tap again to clear", "اضغط مجدداً للمسح"),
        ["clear"] = T("Clear order", "امسح الطلب"),
        ["order"] = T("Order", "طلب"),
        // {n} is the number of dishes; Arabic avoids its plural forms by putting the number after a label.
        ["one"] = T("1 item", "الأصناف: 1"),
        ["many"] = T("{n} items", "الأصناف: {n}"),
    };

    public string? ShareImage =>
        Menu.LogoFile is not null ? $"{Origin}/m/img/{Menu.LogoFile}"
        : Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.ImageFile is not null) is { } first ? $"{Origin}/m/img/{first.ImageFile}"
        : null;

    public string Description =>
        Menu.Tagline ?? T($"{Menu.Title}: the menu, with prices.", $"{Menu.Title}: القائمة والأسعار.");
}

/// <summary>Which branch and order type a public menu request is for; shared by the page and its <c>/items</c> feed.</summary>
public sealed record MenuContext(List<MenuContext.BranchRow> Branches, MenuContext.BranchRow? Branch, List<MenuOrderType> Types, MenuOrderType? Type)
{
    public sealed record BranchRow(Guid SiteId, string Slug, string Name);

    public static async Task<MenuContext> LoadAsync(ControlDbContext db, Menu menu, string? b, int? t)
    {
        var branches = await db.MenuBranches.AsNoTracking().Where(x => x.MenuId == menu.Id && !x.Site.IsDisabled)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => new BranchRow(x.SiteId, x.Slug, x.Site.Name)).ToListAsync();
        // One branch: it is the branch, no question asked. Several: the one named, or none yet.
        var branch = branches.Count == 1 ? branches[0] : branches.FirstOrDefault(x => x.Slug == b?.ToLowerInvariant());
        var types = await db.MenuOrderTypes.AsNoTracking().Where(x => x.MenuId == menu.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        var type = types.FirstOrDefault(x => x.Id == t) ?? types.FirstOrDefault();
        return new MenuContext(branches, branch, types, type);
    }

    /// <summary>Loads just what pricing needs: this branch's row and this type's own price.</summary>
    public static IQueryable<MenuItem> WithPrices(IQueryable<MenuItem> q, Guid? site, MenuOrderType? type)
    {
        var typeId = type?.Id ?? 0;
        return q.Include(i => i.Branches.Where(r => r.SiteId == site)).Include(i => i.TypePrices.Where(p => p.OrderTypeId == typeId));
    }
}
