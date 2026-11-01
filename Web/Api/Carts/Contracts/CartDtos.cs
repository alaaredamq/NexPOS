using System.ComponentModel.DataAnnotations;
using Web.Api.Pos.Contracts;

namespace Web.Api.Carts.Contracts;

/// <summary>Park the current cart so the next customer can be served.</summary>
public sealed class PosHoldCartRequest
{
    [Required]
    public PosCartInput Cart { get; set; } = new();

    /// <summary>Null or 0 is a walk-in customer.</summary>
    public int? CustomerId { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

public sealed record HeldCartDto
{
    public required int Id { get; init; }

    public int? CustomerId { get; init; }

    /// <summary>The customer's name; empty for a walk-in (the UI shows its own localized "Walk-in customer").</summary>
    public required string CustomerName { get; init; }

    public required bool IsWalkIn { get; init; }

    /// <summary>Sum of the lines as parked, in each product's own currency - a rough figure for the list; resuming prices the cart properly.</summary>
    public required decimal TotalAmount { get; init; }

    public required int ItemCount { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required string Notes { get; init; }
}

/// <summary>A line of a resumed cart, in the same terms the till sends it: product + the way it was priced. Send it to <c>pos/price</c> to get current prices.</summary>
public sealed record ResumedLineDto
{
    public required int ProductId { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }

    public decimal? WeightOrVolume { get; init; }

    /// <summary><c>default</c>, <c>tier</c> (one of the product's stored prices) or <c>custom</c>.</summary>
    public required string PriceKind { get; init; }

    public int? PriceTierId { get; init; }

    public string? PriceLabel { get; init; }

    /// <summary>The unit price it was parked at, in the product's currency. For a <c>custom</c> line convert it into the cart currency before sending it as <c>customPrice</c>.</summary>
    public required decimal UnitPrice { get; init; }

    public required int CurrencyId { get; init; }
}

public sealed record ResumedCartDto
{
    public required int CartId { get; init; }

    public int? CustomerId { get; init; }

    public required IReadOnlyList<ResumedLineDto> Lines { get; init; }

    public decimal? DiscountAmount { get; init; }

    public decimal? DiscountPercentage { get; init; }

    public string? DiscountLabel { get; init; }

    public required string Notes { get; init; }
}

public sealed record HeldCartsDeletedDto
{
    public required int DeletedCount { get; init; }
}
