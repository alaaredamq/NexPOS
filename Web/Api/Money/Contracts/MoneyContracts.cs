using System.ComponentModel.DataAnnotations;

namespace Web.Api.Money.Contracts;

// Track A3 (money configuration): currencies, exchange rates, taxes, discounts, payment methods and code lists.
// Every list endpoint takes ?page&pageSize&sort&dir&q (see Web/Api/README.md) and answers { items, total, page, pageSize }.

/// <summary>A currency. <see cref="RateToUsd"/> is what the till converts with (USD itself is 1).</summary>
public sealed record CurrencyDto
{
    public required int Id { get; init; }

    /// <summary>ISO-style code, upper case (<c>USD</c>, <c>LBP</c>).</summary>
    public required string Code { get; init; }

    public required string Name { get; init; }

    /// <summary>The symbol printed next to amounts (<c>$</c>, <c>ل.ل</c>).</summary>
    public required string Notation { get; init; }

    /// <summary>How many units of this currency one US dollar buys. The till converts price A to B as <c>A / rateA * rateB</c>.</summary>
    public required decimal RateToUsd { get; init; }

    /// <summary>Exactly one currency is the default: the one the till opens in and totals are reported in.</summary>
    public required bool IsDefault { get; init; }

    /// <summary>An inactive currency is not offered for new prices or payments; history that uses it is untouched.</summary>
    public required bool IsActive { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC; null while the currency was never edited.</summary>
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>Body of "create / edit a currency".</summary>
public sealed record CurrencyRequest
{
    /// <summary>Trimmed and upper-cased; unique across currencies.</summary>
    [Required]
    [StringLength(10, MinimumLength = 1)]
    public required string Code { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    [Required]
    [StringLength(10, MinimumLength = 1)]
    public required string Notation { get; init; }

    /// <summary>Greater than zero (the till divides by it).</summary>
    [Range(typeof(decimal), "0.000001", "1000000000000")]
    public required decimal RateToUsd { get; init; }

    /// <summary>Making a currency the default un-defaults the previous one. The default cannot be un-defaulted here: make another one the default.</summary>
    public required bool IsDefault { get; init; }

    public required bool IsActive { get; init; }
}

/// <summary>Body of the converter: what the till would show for <see cref="Amount"/> of one currency in another.</summary>
public sealed record CurrencyConvertRequest
{
    [Range(typeof(decimal), "0", "1000000000000")]
    public required decimal Amount { get; init; }

    public required int FromCurrencyId { get; init; }

    public required int ToCurrencyId { get; init; }
}

/// <summary>The converted amount, rounded to 2 decimals with banker's rounding exactly like a till price (<c>PosPricingMath.ToCartCurrency</c>).</summary>
public sealed record CurrencyConversionDto
{
    public required decimal Amount { get; init; }

    public required decimal Converted { get; init; }

    public required string FromCode { get; init; }

    public required string ToCode { get; init; }
}

/// <summary>A pair-specific rate that overrides the USD bridge: <c>1 from = rate to</c>, optionally only for one supplier's products.</summary>
public sealed record ExchangeRateDto
{
    public required int Id { get; init; }

    public required int FromCurrencyId { get; init; }

    public required string FromCurrencyCode { get; init; }

    public required int ToCurrencyId { get; init; }

    public required string ToCurrencyCode { get; init; }

    /// <summary>How many <c>to</c> units one <c>from</c> unit is worth.</summary>
    public required decimal Rate { get; init; }

    /// <summary>Null for the store-wide rate.</summary>
    public int? SupplierId { get; init; }

    public string? SupplierName { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC; null while the rate was never edited.</summary>
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>Body of "create / edit an exchange rate". One rate per (from, to, supplier).</summary>
public sealed record ExchangeRateRequest
{
    public required int FromCurrencyId { get; init; }

    /// <summary>Different from <see cref="FromCurrencyId"/>.</summary>
    public required int ToCurrencyId { get; init; }

    [Range(typeof(decimal), "0.000001", "1000000000000")]
    public required decimal Rate { get; init; }

    public int? SupplierId { get; init; }
}

/// <summary>An id and a display name, for the pickers of the forms.</summary>
public sealed record MoneyOptionDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }
}

/// <summary>A tax rate. <see cref="Type"/>: 0 VAT, 1 sales tax, 2 service tax, 3 excise tax, 4 custom.</summary>
public sealed record TaxRateDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Percentage, 0-100.</summary>
    public required decimal Rate { get; init; }

    /// <summary>0 VAT, 1 sales tax, 2 service tax, 3 excise tax, 4 custom.</summary>
    public required int Type { get; init; }

    public string? TaxCode { get; init; }

    /// <summary>The default rate is the one a calculation without a tax or type picks. At most one.</summary>
    public required bool IsDefault { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>True when prices already include this tax.</summary>
    public required bool IsInclusive { get; init; }

    public required bool ApplyToAllProducts { get; init; }

    public int? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    /// <summary>UTC.</summary>
    public DateTime? EffectiveFrom { get; init; }

    /// <summary>UTC.</summary>
    public DateTime? EffectiveTo { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>Body of "create / edit a tax rate".</summary>
public sealed record TaxRateRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public required decimal Rate { get; init; }

    /// <summary>0 VAT, 1 sales tax, 2 service tax, 3 excise tax, 4 custom.</summary>
    [Range(0, 4)]
    public required int Type { get; init; }

    [StringLength(50)]
    public string? TaxCode { get; init; }

    /// <summary>Making a tax the default un-defaults the previous one.</summary>
    public required bool IsDefault { get; init; }

    public required bool IsActive { get; init; }

    public required bool IsInclusive { get; init; }

    public required bool ApplyToAllProducts { get; init; }

    public int? CategoryId { get; init; }

    public DateTime? EffectiveFrom { get; init; }

    /// <summary>Not before <see cref="EffectiveFrom"/>.</summary>
    public DateTime? EffectiveTo { get; init; }
}

/// <summary>Body of the tax calculator. Name a <see cref="TaxId"/>, or a <see cref="TaxType"/> (the active tax of that type), or neither (the default tax).</summary>
public sealed record TaxCalculationRequest
{
    [Range(typeof(decimal), "0", "1000000000000")]
    public required decimal BaseAmount { get; init; }

    public int? TaxId { get; init; }

    [Range(0, 4)]
    public int? TaxType { get; init; }
}

/// <summary>What a tax adds to a base amount. All zero when the tax is not in force today.</summary>
public sealed record TaxCalculationDto
{
    public required decimal BaseAmount { get; init; }

    public required decimal TaxAmount { get; init; }

    public required decimal TotalAmount { get; init; }
}

/// <summary>A percentage discount the cashier can pick at the till.</summary>
public sealed record DiscountDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Percentage, 0-100.</summary>
    public required decimal Rate { get; init; }

    /// <summary>The discount the till starts with. At most one.</summary>
    public required bool IsDefault { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>Body of "create / edit a discount".</summary>
public sealed record DiscountRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public required decimal Rate { get; init; }

    /// <summary>Making a discount the default un-defaults the previous one.</summary>
    public required bool IsDefault { get; init; }
}

/// <summary>A way to pay at the till.</summary>
public sealed record PaymentMethodItemDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The method the till preselects. At most one.</summary>
    public required bool IsDefault { get; init; }

    /// <summary>
    /// The system method behind "add to customer debt". The till finds it by its name, so it cannot be renamed, made default or deleted,
    /// and the cashier never sees it in the list of tenders.
    /// </summary>
    public required bool IsSystem { get; init; }

    /// <summary>The till works out change for methods whose name contains "cash".</summary>
    public required bool GivesChange { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>Body of "create / edit a payment method".</summary>
public sealed record PaymentMethodRequest
{
    /// <summary>Unique (ignoring case). A name containing "cash" makes the till work out change.</summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>Making a method the default un-defaults the previous one.</summary>
    public required bool IsDefault { get; init; }
}

/// <summary>A code list: a named, keyed set of values other screens can draw choices from.</summary>
public sealed record CodeListDto
{
    public required int Id { get; init; }

    /// <summary>Unique, ignoring case; how other code looks the list up.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required bool IsActive { get; init; }

    public required bool AllowMultipleSelection { get; init; }

    public required int DisplayOrder { get; init; }

    public required int ValueCount { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>A code list with its values.</summary>
public sealed record CodeListDetailsDto
{
    public required CodeListDto List { get; init; }

    public required IReadOnlyList<CodeListValueDto> Values { get; init; }
}

/// <summary>One value of a code list.</summary>
public sealed record CodeListValueDto
{
    public required int Id { get; init; }

    public required int CodeListId { get; init; }

    /// <summary>Unique inside its list, ignoring case.</summary>
    public required string Value { get; init; }

    public string? DisplayText { get; init; }

    public string? Description { get; init; }

    public required bool IsActive { get; init; }

    public required int DisplayOrder { get; init; }

    /// <summary>Free-form JSON a consumer of the list may read.</summary>
    public string? Metadata { get; init; }
}

/// <summary>Body of "create / edit a code list".</summary>
public sealed record CodeListRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Key { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    public required bool IsActive { get; init; }

    public required bool AllowMultipleSelection { get; init; }

    [Range(0, 100000)]
    public required int DisplayOrder { get; init; }
}

/// <summary>Body of "create / edit a code list value".</summary>
public sealed record CodeListValueRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Value { get; init; }

    [StringLength(200)]
    public string? DisplayText { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    public required bool IsActive { get; init; }

    [Range(0, 100000)]
    public required int DisplayOrder { get; init; }

    [StringLength(2000)]
    public string? Metadata { get; init; }
}
