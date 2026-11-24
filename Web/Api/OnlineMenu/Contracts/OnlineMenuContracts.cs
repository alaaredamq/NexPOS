using System.ComponentModel.DataAnnotations;

namespace Web.Api.OnlineMenu.Contracts;

/// <summary>The online-menu screen: its sections in order, and how the shop's online menu is doing.</summary>
public sealed record OnlineMenuDto
{
    public required IReadOnlyList<OnlineMenuSectionDto> Sections { get; init; }

    /// <summary>How many active products the online menu shows (each counted once). With no sections, every active product.</summary>
    public required int ProductsShown { get; init; }

    /// <summary>Active products in no section, so not on the online menu. 0 while there are no sections (the menu then shows everything by category).</summary>
    public required int ProductsLeftOut { get; init; }

    public required OnlineMenuStatusDto Status { get; init; }
}

/// <summary>Whether this till can feed an online menu right now.</summary>
public sealed record OnlineMenuStatusDto
{
    /// <summary>This till is connected to Coduo POS Online (Settings &gt; Online).</summary>
    public required bool Connected { get; init; }

    /// <summary>The cloud says an online menu follows this branch. Null until the till has asked since it started.</summary>
    public bool? Linked { get; init; }

    /// <summary>The menu's public address, when linked.</summary>
    public string? MenuUrl { get; init; }

    /// <summary>Where the owner adds pictures and descriptions: the cloud's menu editor.</summary>
    public string? EditorUrl { get; init; }

    /// <summary>When this till last sent its products to the menu (since it started).</summary>
    public DateTime? LastSentUtc { get; init; }
}

public sealed record OnlineMenuSectionDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>How many picks it holds (products and whole categories). The picks themselves load a page at a time.</summary>
    public required int EntryCount { get; init; }

    /// <summary>How many active products the section shows (whole categories counted by their products).</summary>
    public required int ProductCount { get; init; }
}

/// <summary>One pick in a section: a product, or a whole category.</summary>
public sealed record OnlineMenuEntryDto
{
    /// <summary>Id of the pick (not of the product or category); used to remove or reorder it.</summary>
    public required int Id { get; init; }

    /// <summary><c>product</c> or <c>category</c>.</summary>
    public required string Kind { get; init; }

    public int? ProductId { get; init; }

    public int? CategoryId { get; init; }

    public required string Name { get; init; }

    /// <summary>Product: its selling price. Category: null.</summary>
    public decimal? Price { get; init; }

    public string? CurrencyNotation { get; init; }

    public string? Image { get; init; }

    /// <summary>Category: how many active products it brings. Product: 1, or 0 when the product is switched off.</summary>
    public required int ProductCount { get; init; }

    /// <summary>Product: false when it is switched off or deleted, so the menu skips it for now.</summary>
    public required bool IsActive { get; init; }
}

/// <summary>One page of a section's picks.</summary>
public sealed record OnlineMenuEntriesDto
{
    public required IReadOnlyList<OnlineMenuEntryDto> Items { get; init; }

    public required bool HasMore { get; init; }
}

/// <summary>Moves a pick one step within its section.</summary>
public sealed record OnlineMenuMoveRequest
{
    /// <summary>-1 = up, 1 = down.</summary>
    [Range(-1, 1)]
    public required int Direction { get; init; }
}

/// <summary>A product to pick, and where it is on the menu now (if anywhere).</summary>
public sealed record OnlineMenuProductOptionDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? CategoryName { get; init; }

    public required decimal Price { get; init; }

    public required string CurrencyNotation { get; init; }

    public string? Image { get; init; }

    /// <summary>The section it was picked into on its own, if any.</summary>
    public int? SectionId { get; init; }

    public string? SectionName { get; init; }

    /// <summary>The section it shows in through its category, if any (only when not picked on its own).</summary>
    public string? ViaCategorySectionName { get; init; }
}

public sealed record OnlineMenuProductOptionsDto
{
    public required IReadOnlyList<OnlineMenuProductOptionDto> Items { get; init; }

    public required bool HasMore { get; init; }
}

public sealed record OnlineMenuCategoryOptionDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required int ActiveProducts { get; init; }

    /// <summary>The section it was picked into, if any.</summary>
    public int? SectionId { get; init; }

    public string? SectionName { get; init; }
}

public sealed record OnlineMenuSectionRequest
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public required string Name { get; init; }
}

/// <summary>Products and whole categories to put at the end of a section. Ones already in another section move here.</summary>
public sealed record OnlineMenuPlaceRequest
{
    public IReadOnlyList<int>? ProductIds { get; init; }

    public IReadOnlyList<int>? CategoryIds { get; init; }
}

public sealed record OnlineMenuOrderRequest
{
    [Required]
    [MinLength(1)]
    public required IReadOnlyList<int> Ids { get; init; }
}
