namespace Cloud.Data;

/// <summary>
/// A shop's public online menu: a page anyone can open at /m/{slug} (a QR code on the table, a link in a bio) that lists what the shop
/// sells, with pictures and prices. One per client. It is filled by hand in the menu editor, and/or from the POS of any of the shop's
/// branches (<see cref="Branches"/>): each linked shop PC publishes its product names, prices and categories here and keeps them current.
/// With several branches the guest picks one (or a table QR picks it): the dishes are shared, the prices and what is sold are that branch's.
/// This is the one place where shop data reaches the cloud readable, and only because the owner chose to publish it to the world.
/// </summary>
public class Menu
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    /// <summary>The address: /m/{Slug}. Lower-case letters, digits and dashes.</summary>
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>One line under the title ("Burgers and shakes since 2009").</summary>
    public string? Tagline { get; set; }
    /// <summary>Shown at the bottom: address, opening hours, phone.</summary>
    public string? Note { get; set; }
    /// <summary><c>en</c> or <c>ar</c>: the page's direction and its few fixed words.</summary>
    public string Language { get; set; } = "en";
    /// <summary>For items added by hand: what their prices are in ("$", "LBP").</summary>
    public string Currency { get; set; } = "$";
    /// <summary>How the dishes are laid out on the public page; one of <see cref="MenuListingStyles.All"/>. Each shop picks its own.</summary>
    public string ListingStyle { get; set; } = MenuListingStyles.Default;
    public string? LogoFile { get; set; }
    /// <summary>Off = the address answers 404, so the owner can prepare the menu before sharing it.</summary>
    public bool IsPublished { get; set; }
    /// <summary>Random letters that open the page before it is published (?preview=...), so the owner can check it first.</summary>
    public string PreviewKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<MenuSection> Sections { get; set; } = [];
    public List<MenuItem> Items { get; set; } = [];
    /// <summary>The branches whose POS fills this menu, in the order the guest sees them. The first one is the main branch.</summary>
    public List<MenuBranch> Branches { get; set; } = [];
    /// <summary>Dine in, take out, delivery...: made by the owner; each can price dishes differently. None = one price.</summary>
    public List<MenuOrderType> OrderTypes { get; set; } = [];
}

/// <summary>
/// A branch whose POS fills the menu. Its shop PC sends its product list; the cloud keeps that branch's prices and which dishes it sells.
/// The first branch (lowest <see cref="SortOrder"/>) is the main one: it decides where a dish sits on the menu and, when its till arranges
/// the menu, the order. The others only add what the main one lacks.
/// </summary>
public class MenuBranch
{
    public int Id { get; set; }
    public int MenuId { get; set; }
    public Menu Menu { get; set; } = null!;
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;
    /// <summary>In the address when the guest picks this branch: /m/{menu}?b={Slug}. Unique within the menu.</summary>
    public string Slug { get; set; } = "";
    public int SortOrder { get; set; }
    /// <summary>The fingerprint of the last product list this branch sent, so it only sends again when something changed.</summary>
    public string? CatalogHash { get; set; }
    public DateTime? CatalogAt { get; set; }
    /// <summary>This branch's till decides the order of its sections and items (its online-menu screen is in use).</summary>
    public bool PosArranged { get; set; }
}

/// <summary>A heading on the menu ("Burgers"). One that came from a POS takes its name from it.</summary>
public class MenuSection
{
    public int Id { get; set; }
    public int MenuId { get; set; }
    public Menu Menu { get; set; } = null!;
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    /// <summary>Set when a POS made it: what it mirrors at the branch that made it (<c>s12</c> = online-menu section 12, <c>c5</c> =
    /// category 5); null = made by hand. Branches number their categories differently, so across branches a section is known by its name.</summary>
    public string? PosKey { get; set; }
}

/// <summary>
/// One dish or product, shared by all branches. A POS one (it has <see cref="Branches"/>) takes its name and section from the POS and
/// shows only at the branches that sell it, at their price; the owner adds the picture, a description and whether it shows.
/// A hand-made one (no branches) shows everywhere at <see cref="Price"/>.
/// </summary>
public class MenuItem
{
    public int Id { get; set; }
    public int MenuId { get; set; }
    public Menu Menu { get; set; } = null!;
    public int? SectionId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>The owner's own words. Wins over <see cref="PosDescription"/>.</summary>
    public string? Description { get; set; }
    /// <summary>The description the POS has for the product, if any.</summary>
    public string? PosDescription { get; set; }
    /// <summary>A hand-made dish's price; for a POS one, the main branch's (shown where no branch is chosen, e.g. in the editor).</summary>
    public decimal Price { get; set; }
    /// <summary>The currency as the shop writes it ("$", "LBP"); null = the menu's <see cref="Menu.Currency"/>.</summary>
    public string? Currency { get; set; }
    public string? ImageFile { get; set; }
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    public bool IsSoldOut { get; set; }

    /// <summary>The branches that sell it, each with its own price. Empty = made by hand.</summary>
    public List<MenuItemBranch> Branches { get; set; } = [];
    /// <summary>Fixed prices for some order types (delivery...); a type without one prices the dish by its own rule.</summary>
    public List<MenuItemTypePrice> TypePrices { get; set; } = [];
}

/// <summary>A dish as one branch's POS sells it: that branch's product and price.</summary>
public class MenuItemBranch
{
    public int Id { get; set; }
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public Guid SiteId { get; set; }
    /// <summary>The product's id in that branch's POS (each branch numbers its products its own way).</summary>
    public int PosProductId { get; set; }
    public decimal Price { get; set; }
    public string? Currency { get; set; }
}

/// <summary>
/// A way of ordering (dine in, take out, delivery...) that the owner makes. The guest picks one on the menu and every price follows it:
/// a dish's own price for this type when it has one (<see cref="MenuItemTypePrice"/>), otherwise its normal price changed by
/// <see cref="Adjust"/>. The POS has no such types, so they live here and work on top of each branch's POS prices.
/// </summary>
public class MenuOrderType
{
    public int Id { get; set; }
    public int MenuId { get; set; }
    public Menu Menu { get; set; } = null!;
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    /// <summary>One of <see cref="MenuPriceAdjust"/>: the normal price, a percentage on top (or off), or a fixed amount on top (or off).</summary>
    public string Adjust { get; set; } = MenuPriceAdjust.None;
    /// <summary>The percentage (10 = +10%) or amount (1.5 = +1.50), when <see cref="Adjust"/> uses one. Negative takes off.</summary>
    public decimal AdjustValue { get; set; }
}

/// <summary>A dish's own price for one order type, the same at every branch.</summary>
public class MenuItemTypePrice
{
    public int Id { get; set; }
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int OrderTypeId { get; set; }
    public MenuOrderType OrderType { get; set; } = null!;
    public decimal Price { get; set; }
}

public static class MenuPriceAdjust
{
    public const string None = "none";
    public const string Percent = "percent";
    public const string Amount = "amount";
    public static bool IsValid(string? s) => s is None or Percent or Amount;

    /// <summary>The price for an order type: the dish's own price for it if set, else the normal price changed by the type's rule. Never below 0.</summary>
    public static decimal Apply(decimal normal, MenuOrderType? type, decimal? own)
    {
        if (own is { } fixedPrice) return fixedPrice;
        if (type is null) return normal;
        var price = type.Adjust switch
        {
            Percent => Math.Round(normal * (1 + type.AdjustValue / 100m), 2, MidpointRounding.AwayFromZero),
            Amount => normal + type.AdjustValue,
            _ => normal,
        };
        return Math.Max(0, price);
    }
}

/// <summary>The ways a public menu can list its dishes. The page draws them all from the same markup; only menu.css differs.</summary>
public static class MenuListingStyles
{
    /// <summary>Round thumbnails, one line of description, price on the right: dense, and fine with or without pictures.</summary>
    public const string Compact = "compact";
    /// <summary>Two columns of square picture cards.</summary>
    public const string Grid = "grid";
    /// <summary>One large picture card per dish, with its description.</summary>
    public const string Cards = "cards";
    /// <summary>No pictures: name, dotted leader, price, like a printed menu.</summary>
    public const string Classic = "classic";
    /// <summary>Each section a row of cards that swipes sideways.</summary>
    public const string Rails = "rails";

    public const string Default = Compact;

    public static readonly string[] All = [Compact, Grid, Cards, Classic, Rails];

    public static bool IsValid(string? style) => style is not null && All.Contains(style);
}
