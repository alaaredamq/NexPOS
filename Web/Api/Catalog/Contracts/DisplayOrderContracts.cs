using System.ComponentModel.DataAnnotations;

namespace Web.Api.Catalog.Contracts;

/// <summary>The products shown on the POS main screen, in order, with the tiles above the table.</summary>
public sealed record DisplayOrderDto
{
    public required IReadOnlyList<DisplayOrderItemDto> Items { get; init; }

    public required int Total { get; init; }

    public required int Active { get; init; }

    public required int Inactive { get; init; }

    /// <summary>Items whose product stock is at or below its minimum stock.</summary>
    public required int LowStock { get; init; }
}

/// <summary>One product on the POS main screen.</summary>
public sealed record DisplayOrderItemDto
{
    /// <summary>Id of the display-order row (not of the product); used to remove it.</summary>
    public required int Id { get; init; }

    public required int ProductId { get; init; }

    /// <summary>1-based position on the main screen.</summary>
    public required int Order { get; init; }

    public required bool IsActive { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Product picture URL; the POS no longer shows pictures, this page still does.</summary>
    public string? Image { get; init; }

    public string? CategoryName { get; init; }

    public required decimal Price { get; init; }

    public required string CurrencyNotation { get; init; }

    public required decimal StockQuantity { get; init; }

    public required bool IsLowStock { get; init; }
}

/// <summary>New order of the main screen: the ids of the display-order rows, first to last.</summary>
public sealed record ReorderRequest
{
    [Required]
    [MinLength(1)]
    public required IReadOnlyList<int> Ids { get; init; }
}

/// <summary>Body of "add an existing product to the main screen".</summary>
public sealed record AddToDisplayOrderRequest
{
    [Range(1, int.MaxValue)]
    public required int ProductId { get; init; }
}

/// <summary>A product that is not on the main screen yet; one page of the picker.</summary>
public sealed record AvailableProductsDto
{
    public required IReadOnlyList<AvailableProductDto> Items { get; init; }

    /// <summary>Whether another page exists after these rows.</summary>
    public required bool HasMore { get; init; }
}

public sealed record AvailableProductDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Image { get; init; }

    public string? CategoryName { get; init; }

    public required decimal Price { get; init; }

    public required string CurrencyNotation { get; init; }
}

/// <summary>Body of the quick-add: a basic product created and put on the main screen in one step.</summary>
public sealed record QuickAddProductRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    /// <summary>Leave empty (or set <see cref="GenerateBarcode"/>) to get a generated EAN-13.</summary>
    [StringLength(50)]
    public string? Barcode { get; init; }

    public bool GenerateBarcode { get; init; }

    [Range(0.01, 999999999)]
    public required decimal Price { get; init; }

    /// <summary>0 or missing means the default currency.</summary>
    public int CurrencyId { get; init; }

    [Range(0, 999999999)]
    public decimal StockQuantity { get; init; }
}

public sealed record QuickAddResultDto
{
    public required int ProductId { get; init; }

    public required int DisplayOrderId { get; init; }

    /// <summary>The barcode the product ended up with (generated or typed).</summary>
    public required string Barcode { get; init; }
}

/// <summary>What the quick-add form needs to fill its currency picker.</summary>
public sealed record QuickAddOptionsDto
{
    public required IReadOnlyList<CurrencyOptionDto> Currencies { get; init; }

    /// <summary>Id of the default currency, or 0 when none is marked.</summary>
    public required int DefaultCurrencyId { get; init; }
}

public sealed record CurrencyOptionDto
{
    public required int Id { get; init; }

    public required string Code { get; init; }

    public required string Notation { get; init; }
}
