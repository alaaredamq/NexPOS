namespace Cloud.Data;

/// <summary>
/// A shop's public online menu: a page anyone can open at /m/{slug} (a QR code on the table, a link in a bio) that lists what the shop
/// sells, with pictures and prices. One per client. It is filled either by hand in the menu editor, or from the shop's POS when
/// <see cref="LinkedSiteId"/> is set: the shop PC then publishes its product names, prices and categories here and keeps them current.
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
    /// <summary>The branch whose POS products fill this menu; null = filled by hand only.</summary>
    public Guid? LinkedSiteId { get; set; }
    /// <summary>The fingerprint of the last product list the shop PC sent, so it only sends again when something changed.</summary>
    public string? CatalogHash { get; set; }
    public DateTime? CatalogAt { get; set; }
    /// <summary>The POS decides the order of its sections and items (its online-menu screen is in use), so the editor does not offer to reorder them.</summary>
    public bool PosArranged { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<MenuSection> Sections { get; set; } = [];
    public List<MenuItem> Items { get; set; } = [];
}

/// <summary>A heading on the menu ("Burgers"). A linked one mirrors a POS category and takes its name from it.</summary>
public class MenuSection
{
    public int Id { get; set; }
    public int MenuId { get; set; }
    public Menu Menu { get; set; } = null!;
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    /// <summary>What it mirrors on the POS: <c>s12</c> (online-menu section 12) or <c>c5</c> (category 5); null = made by hand.</summary>
    public string? PosKey { get; set; }
}

/// <summary>
/// One dish or product. A linked one (<see cref="PosProductId"/> set) takes its name, price and section from the POS, and the owner
/// adds the picture, a description and whether it shows; a hand-made one is edited freely.
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
    public decimal Price { get; set; }
    /// <summary>The currency as the shop writes it ("$", "LBP"); null = the menu's <see cref="Menu.Currency"/>.</summary>
    public string? Currency { get; set; }
    public string? ImageFile { get; set; }
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    public bool IsSoldOut { get; set; }
    public int? PosProductId { get; set; }
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
