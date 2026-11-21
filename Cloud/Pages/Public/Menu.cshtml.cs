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
public class MenuModel(ControlDbContext db, PortalFormat clock) : PageModel
{
    public const int FirstPaint = 60;

    public sealed record Group(MenuSection? Section, List<MenuItem> Items, int Total)
    {
        public int Key => Section?.Id ?? 0;
        public int Remaining => Total - Items.Count;
    }

    public sealed record BranchChoice(string Slug, string Name, string? Address, string? Status, bool? Open);

    /// <summary>A way to reach the shop, drawn as a round button in the header: what it is (an icon id), its label, and where it goes.</summary>
    public sealed record Action(string Icon, string Label, string Href, bool External);

    /// <summary>A line of the hours table: the day(s), their hours, and whether today is among them.</summary>
    public sealed record HoursRow(string Days, string Hours, bool Today);

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
        _local = clock.Local(clock.Now);
        BranchChoice Choice(MenuContext.BranchRow x)
        {
            var status = MenuHours.At(x.Hours ?? menu.Hours, _local);
            return new BranchChoice(x.Slug, x.Name, x.Address ?? menu.Address, StatusText(status), status?.Open);
        }
        Branches.AddRange(context.Branches.Count > 1 ? context.Branches.Select(Choice) : []);
        if (context.Branch is { } chosen) { Branch = Choice(chosen); _site = chosen.SiteId; _place = chosen; }
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
        // Sending the order to the shop, and following it.
        ["needName"] = T("Write your name, so the staff know whose order it is.", "اكتب اسمك ليعرف العاملون صاحب الطلب."),
        ["sending"] = T("Sending…", "جارٍ الإرسال…"),
        ["sentTitle"] = T("Order sent", "أُرسل الطلب"),
        ["netErr"] = T("Could not send. Check your connection and try again.", "تعذّر الإرسال. تحقق من الاتصال وحاول مجدداً."),
        ["number"] = T("Order #{n}", "الطلب رقم {n}"),
        ["st_new"] = T("Sent. Waiting for the shop to accept it.", "أُرسل. بانتظار قبول المحل."),
        ["st_accepted"] = T("Accepted. It is being prepared.", "قُبل. يجري تحضيره."),
        ["st_ready"] = T("Ready!", "جاهز!"),
        ["st_done"] = T("Done. Thank you!", "تم. شكراً لك!"),
        ["st_rejected"] = T("The shop could not take this order. Please ask a member of staff.", "تعذّر على المحل قبول هذا الطلب. يرجى سؤال أحد العاملين."),
        ["sh_new"] = T("Waiting", "بالانتظار"),
        ["sh_accepted"] = T("Accepted", "مقبول"),
        ["sh_ready"] = T("Ready", "جاهز"),
        ["sh_done"] = T("Done", "تم"),
        ["sh_rejected"] = T("Turned down", "مرفوض"),
    };

    public string? ShareImage =>
        Menu.CoverFile is not null ? $"{Origin}/m/img/{Menu.CoverFile}"
        : Menu.LogoFile is not null ? $"{Origin}/m/img/{Menu.LogoFile}"
        : Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.ImageFile is not null) is { } first ? $"{Origin}/m/img/{first.ImageFile}"
        : null;

    public string Description =>
        Menu.Tagline ?? T($"{Menu.Title}: the menu, with prices.", $"{Menu.Title}: القائمة والأسعار.");

    // ---- the shop: its look, where it is, when it is open, how to reach it ----

    private DateTime _local;
    private MenuContext.BranchRow? _place;

    public string Theme => MenuThemes.IsValid(Menu.Theme) ? Menu.Theme : MenuThemes.Default;

    /// <summary>The Google Fonts stylesheet for this theme's headings (each theme has its own), plus the body, figures and Arabic faces.</summary>
    public string FontHref => "https://fonts.googleapis.com/css2?" + Theme switch
    {
        MenuThemes.Ember => "family=Barlow+Condensed:wght@600;700;800",
        MenuThemes.Saffron => "family=Fraunces:opsz,wght@9..144,600..800",
        MenuThemes.Harbor => "family=Sora:wght@600;700;800",
        MenuThemes.Plum => "family=Playfair+Display:wght@600..800",
        MenuThemes.Charcoal => "family=Instrument+Serif:ital@0;1",
        _ => "family=Bricolage+Grotesque:opsz,wght@12..96,600..800",
    } + "&family=Geist:wght@400;500;600&family=Geist+Mono:wght@500;600&family=Noto+Sans+Arabic:wght@400;500;600;700;800&display=swap";

    // A branch's own details win; what it leaves empty comes from the shop.
    public string? Address => _place?.Address ?? Menu.Address;
    public string? Phone => _place?.Phone ?? Menu.Phone;
    private string? HoursText => _place?.Hours ?? Menu.Hours;
    public string? MapLink => MenuContact.MapLink(_place?.MapUrl ?? (_place?.Address is null ? Menu.MapUrl : null), Address);

    public MenuHours.Status? Status => MenuHours.At(HoursText, _local);
    public string? StatusLine => StatusText(Status);

    private static readonly string[] DaysEn = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] DaysAr = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];
    private static readonly string[] ShortEn = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private string Day(int d) => Arabic ? DaysAr[d] : DaysEn[d];
    private static string Clock(TimeOnly t) => t.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"Open now · until 23:00", "Closed · opens 09:00", "Closed · opens Monday 09:00"; null when no hours were given.</summary>
    private string? StatusText(MenuHours.Status? s)
    {
        if (s is null) return null;
        if (s.Open) return s.Until is { } u ? T($"Open now · until {Clock(u)}", $"مفتوح الآن · حتى {Clock(u)}") : T("Open now", "مفتوح الآن");
        if (s.Opens is not { } o) return T("Closed", "مغلق");
        var day = s.OpensOn == _local.DayOfWeek ? "" : (s.OpensOn == (DayOfWeek)(((int)_local.DayOfWeek + 1) % 7) ? T("tomorrow ", "غداً ") : Day((int)s.OpensOn!) + " ");
        return T($"Closed · opens {day}{Clock(o)}", $"مغلق · يفتح {day}{Clock(o)}");
    }

    /// <summary>The week, Monday first, with days that keep the same hours run together ("Mon – Fri").</summary>
    public List<HoursRow> HoursTable
    {
        get
        {
            var week = MenuHours.Parse(HoursText);
            var rows = new List<HoursRow>();
            if (week is null) return rows;
            var order = new[] { 1, 2, 3, 4, 5, 6, 0 };
            string Text(int d) => week[d].Count == 0 ? T("Closed", "مغلق") : string.Join(", ", week[d].Select(r => $"{Clock(r.Open)}–{Clock(r.Close)}"));
            string Name(int d) => Arabic ? DaysAr[d] : ShortEn[d];
            var today = (int)_local.DayOfWeek;
            for (var i = 0; i < order.Length;)
            {
                var j = i;
                while (j + 1 < order.Length && Text(order[j + 1]) == Text(order[i])) j++;
                var days = i == j ? (Arabic ? DaysAr[order[i]] : DaysEn[order[i]]) : $"{Name(order[i])} – {Name(order[j])}";
                rows.Add(new HoursRow(days, Text(order[i]), order[i..(j + 1)].Contains(today)));
                i = j + 1;
            }
            return rows;
        }
    }

    /// <summary>The header's buttons: call, WhatsApp, directions, then the shop's pages. Only what the owner filled in.</summary>
    public List<Action> Actions
    {
        get
        {
            var list = new List<Action>();
            void Add(string icon, string label, string? href, bool external = true) { if (href is not null) list.Add(new Action(icon, label, href, external)); }
            Add("phone", T("Call", "اتصل"), MenuContact.PhoneLink(Phone), false);
            Add("whatsapp", "WhatsApp", MenuContact.WhatsAppLink(Menu.WhatsApp));
            Add("pin", T("Directions", "الموقع"), MapLink);
            Add("instagram", "Instagram", MenuContact.InstagramLink(Menu.Instagram));
            Add("facebook", "Facebook", MenuContact.FacebookLink(Menu.Facebook));
            Add("tiktok", "TikTok", MenuContact.TikTokLink(Menu.TikTok));
            Add("globe", T("Website", "الموقع الإلكتروني"), MenuContact.WebsiteLink(Menu.Website));
            return list;
        }
    }

    /// <summary>Digits of the WhatsApp number, for sending the order there.</summary>
    public string? WhatsAppDigits => string.IsNullOrWhiteSpace(Menu.WhatsApp) ? null : new string(Menu.WhatsApp.Where(char.IsAsciiDigit).ToArray());
    /// <summary>The order sheet's WhatsApp button: the owner left it on, and there is a number.</summary>
    public string? OrderWhatsApp => Menu.ShowWhatsApp ? WhatsAppDigits : null;
    /// <summary>Whether "Send order" sends it to the shop (its order list or its till), or the order is only shown to staff.</summary>
    public bool TakesOrders => Menu.OrderMode is MenuOrderModes.Request or MenuOrderModes.Pos;

    /// <summary>Whether there is anything for the "Find us" part at the bottom.</summary>
    public bool HasVisit => Address is not null || Phone is not null || HoursTable.Count > 0 || Menu.Note is not null;
}

/// <summary>Which branch and order type a public menu request is for; shared by the page and its <c>/items</c> feed.</summary>
public sealed record MenuContext(List<MenuContext.BranchRow> Branches, MenuContext.BranchRow? Branch, List<MenuOrderType> Types, MenuOrderType? Type)
{
    public sealed record BranchRow(Guid SiteId, string Slug, string Name, string? Address = null, string? MapUrl = null, string? Phone = null, string? Hours = null);

    public static async Task<MenuContext> LoadAsync(ControlDbContext db, Menu menu, string? b, int? t)
    {
        var branches = await db.MenuBranches.AsNoTracking().Where(x => x.MenuId == menu.Id && !x.Site.IsDisabled)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new BranchRow(x.SiteId, x.Slug, x.Site.Name, x.Address, x.MapUrl, x.Phone, x.Hours)).ToListAsync();
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
