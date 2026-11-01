using System.ComponentModel.DataAnnotations;

namespace Web.Api.Suppliers.Contracts;

// Contract of track C3 (Suppliers). Mirrors the Razor SuppliersController; see docs/react-migration/notes/C3.md for the differences.

/// <summary>The kind of a debt-ledger entry.</summary>
public static class SupplierDebtKind
{
    /// <summary>Goods or services taken on credit.</summary>
    public const string Purchase = "purchase";

    /// <summary>A payment to the supplier.</summary>
    public const string Payment = "payment";

    /// <summary>A manual correction typed in by staff (everything entered through this API).</summary>
    public const string Adjustment = "adjustment";
}

/// <summary>One supplier, as the list and the details page show it.</summary>
public sealed record SupplierDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }

    public string? Address { get; init; }

    public string? City { get; init; }

    public string? Country { get; init; }

    public string? PostalCode { get; init; }

    /// <summary>Percent, 0 to 100.</summary>
    public required double DiscountRate { get; init; }

    public required bool IsActive { get; init; }

    public required int CurrencyId { get; init; }

    /// <summary>Name of the supplier's currency (<c>US Dollar</c>).</summary>
    public string? CurrencyName { get; init; }

    /// <summary>Short symbol of the supplier's currency (<c>$</c>); empty when the currency row is gone.</summary>
    public required string CurrencyNotation { get; init; }

    /// <summary>Positive: the business owes the supplier. Negative: the supplier owes the business. In the supplier's currency.</summary>
    public required decimal Balance { get; init; }

    /// <summary>Products that point at this supplier (deleted ones not counted).</summary>
    public required int ProductCount { get; init; }
}

/// <summary>The tiles above the suppliers list.</summary>
public sealed record SupplierSummaryDto
{
    public required int Total { get; init; }

    public required int Active { get; init; }

    public required int Inactive { get; init; }

    /// <summary>Suppliers with a balance other than zero.</summary>
    public required int WithBalance { get; init; }
}

/// <summary>One currency choice of the supplier form.</summary>
public sealed record SupplierCurrencyOptionDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string Notation { get; init; }
}

/// <summary>What the supplier form needs besides the supplier.</summary>
public sealed record SupplierOptionsDto
{
    /// <summary>Active currencies.</summary>
    public required IReadOnlyList<SupplierCurrencyOptionDto> Currencies { get; init; }

    /// <summary>The currency a new supplier starts with (the shop's default), or null when none is marked default.</summary>
    public int? DefaultCurrencyId { get; init; }
}

/// <summary>Body of create and update.</summary>
public sealed record SupplierRequest
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    public required string Name { get; init; }

    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; init; }

    [Phone]
    [StringLength(20)]
    public string? Phone { get; init; }

    [StringLength(500)]
    public string? Address { get; init; }

    [StringLength(100)]
    public string? City { get; init; }

    [StringLength(100)]
    public string? Country { get; init; }

    [StringLength(20)]
    public string? PostalCode { get; init; }

    [Range(0, 100)]
    public double DiscountRate { get; init; }

    [Range(1, int.MaxValue)]
    public required int CurrencyId { get; init; }

    public bool IsActive { get; init; } = true;
}

/// <summary>One entry of a supplier's debt ledger, newest first.</summary>
public sealed record SupplierDebtEntryDto
{
    public required int Id { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>See <see cref="SupplierDebtKind"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Signed: positive raised what the business owes, negative lowered it.</summary>
    public required decimal Amount { get; init; }

    public string? Note { get; init; }

    /// <summary>Who entered it.</summary>
    public string? UserName { get; init; }
}

/// <summary>Body of a manual debt entry.</summary>
public sealed record SupplierDebtRequest
{
    [Range(0.01, 1_000_000_000_000)]
    public required decimal Amount { get; init; }

    /// <summary>True: the business owes the supplier more (balance goes up). False: the business pays something off, or the supplier owes the business (balance goes down).</summary>
    public required bool IOweMore { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }
}

/// <summary>What a debt entry did.</summary>
public sealed record SupplierDebtResultDto
{
    public required SupplierDebtEntryDto Entry { get; init; }

    /// <summary>The supplier's balance after the entry.</summary>
    public required decimal Balance { get; init; }
}
