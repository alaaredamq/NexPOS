using System.ComponentModel.DataAnnotations;

namespace Web.Api.Catalog.Contracts;

/// <summary>One row of the categories list. A category has no colour or image of its own (the POS tints tiles from the name).</summary>
public sealed record CategoryDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Inactive categories stay on their products but are hidden from the POS filters.</summary>
    public required bool IsActive { get; init; }

    /// <summary>Products in the category, deleted ones not counted.</summary>
    public required int ProductCount { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>The four tiles above the categories list.</summary>
public sealed record CategorySummaryDto
{
    public required int Total { get; init; }

    public required int Active { get; init; }

    public required int Inactive { get; init; }

    /// <summary>Products across all categories.</summary>
    public required int Products { get; init; }
}

/// <summary>A category with its products (first 20) and stock figures, for the details page.</summary>
public sealed record CategoryDetailsDto
{
    public required CategoryDto Category { get; init; }

    public required int ActiveProducts { get; init; }

    /// <summary>Sum of the stock quantities of the category's products.</summary>
    public required decimal TotalStock { get; init; }

    /// <summary>Stock x selling price of the category's products, in the default currency's notation (the old page's figure; no conversion).</summary>
    public required decimal TotalValue { get; init; }

    /// <summary>Notation of the default currency (<c>$</c>, <c>L.L.</c>), for <see cref="TotalValue"/>.</summary>
    public required string CurrencyNotation { get; init; }

    /// <summary>First 20 products by name.</summary>
    public required IReadOnlyList<CategoryProductDto> Products { get; init; }
}

/// <summary>A product row inside the category details page.</summary>
public sealed record CategoryProductDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string Barcode { get; init; }

    public required decimal SellingPrice { get; init; }

    /// <summary>Notation of the product's own currency.</summary>
    public required string CurrencyNotation { get; init; }

    public required decimal StockQuantity { get; init; }

    public decimal? MinimumStock { get; init; }

    public required bool IsActive { get; init; }
}

/// <summary>Body of create and update.</summary>
public sealed record CategoryRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public required string Name { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    public bool IsActive { get; init; } = true;
}
