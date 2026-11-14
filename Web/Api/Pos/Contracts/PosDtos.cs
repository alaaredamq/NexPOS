using System.ComponentModel.DataAnnotations;

namespace Web.Api.Pos.Contracts;

// The contract of the point of sale (track P1 of REACT_MIGRATION.md). P2 (catalog), P3 (cart / pricing) and P4 (checkout)
// all build against these. Additive changes are free; a breaking change needs a note in the other halves' notes files.
//
// Money rules in one place:
//  * Every amount is a JSON number (decimal on the server). Prices are always in the CART currency (PosCartInput.CurrencyId)
//    unless a field says "product currency". The server decides amounts; the browser may preview but never has the last word.
//  * Unit prices converted into the cart currency are rounded to 2 decimals with Math.Round (banker's rounding), exactly like the
//    old POS ConvertPrice did. Line totals are NOT rounded (unit price x quantity); Total is rounded to 2 decimals.
//  * Quantity is a whole number of pieces. A product sold by weight or volume sends WeightOrVolume in the product's BASE unit
//    (kg or L) as well - the unit conversion (300 g -> 0.3 kg) stays in the browser.

/// <summary>One product as the POS shows and sells it.</summary>
public sealed record PosProductDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Empty when the product has no barcode.</summary>
    public required string Barcode { get; init; }

    public required int CategoryId { get; init; }

    /// <summary>The category name; the tile colour is derived from <see cref="CategoryId"/> by the client, never stored.</summary>
    public required string CategoryName { get; init; }

    /// <summary>The currency <see cref="SellingPrice"/> is in.</summary>
    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>The default unit price, in the product's currency.</summary>
    public required decimal SellingPrice { get; init; }

    /// <summary>Supplier discount, 0-100. Informational (price check); a sale is priced from <see cref="SellingPrice"/>, a price tier or a custom price.</summary>
    public required double SupplierDiscountRate { get; init; }

    /// <summary>On hand, in the product's unit (pieces, or kg / L).</summary>
    public required decimal StockQuantity { get; init; }

    /// <summary>The level at or below which the tile says "low stock"; null means the till's default of 5.</summary>
    public decimal? MinimumStock { get; init; }

    /// <summary>The product's own unit as stored (<c>piece</c>, <c>kg</c>, <c>liter</c> ...). Null means pieces.</summary>
    public string? Unit { get; init; }

    /// <summary><c>count</c>, <c>weight</c> or <c>volume</c>: whether the cashier types a weight/volume and <c>weightOrVolume</c> must be sent.</summary>
    public required string UnitKind { get; init; }

    public required bool IsPack { get; init; }

    public int? PackSize { get; init; }

    public int? SingleProductId { get; init; }

    /// <summary>False for packs: they have no stored extra prices to choose from.</summary>
    public required bool CanChangePrice { get; init; }
}

public sealed record PosCategoryDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Active products in the category.</summary>
    public required int ProductCount { get; init; }
}

/// <summary>A page of the product grid.</summary>
public sealed record PosProductPageDto
{
    public required IReadOnlyList<PosProductDto> Items { get; init; }

    public required int Total { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    /// <summary>True when the list is the shop's configured "main screen" order (display order); false when it is every active product.</summary>
    public required bool IsMainScreen { get; init; }
}

/// <summary>Result of a product search: one exact barcode hit, or a page of name matches.</summary>
public sealed record PosSearchResultDto
{
    /// <summary><c>barcode</c> (exactly one item) or <c>name</c>.</summary>
    public required string SearchType { get; init; }

    public required IReadOnlyList<PosProductDto> Items { get; init; }

    /// <summary>Another page of name matches exists: ask again with <c>skip</c> = number of items received so far.</summary>
    public required bool HasMore { get; init; }
}

public sealed record PosPriceOptionDto
{
    /// <summary>The stored price's id; null for the product's default selling price.</summary>
    public int? Id { get; init; }

    /// <summary>Empty for the default price (the UI labels it).</summary>
    public required string Name { get; init; }

    /// <summary>In the requested currency, not rounded.</summary>
    public required decimal Price { get; init; }
}

/// <summary>The prices a cashier can choose for one product, converted into <see cref="CurrencyId"/>.</summary>
public sealed record PosPriceOptionsDto
{
    public required int ProductId { get; init; }

    public required int CurrencyId { get; init; }

    /// <summary>The lowest custom unit price allowed (the purchase price), in <see cref="CurrencyId"/>.</summary>
    public required decimal MinPrice { get; init; }

    /// <summary>False when no purchase price is on file; then any price above zero is accepted.</summary>
    public required bool HasCost { get; init; }

    public required IReadOnlyList<PosPriceOptionDto> Options { get; init; }
}

public sealed record PosCurrencyDto
{
    public required int Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Notation { get; init; }

    /// <summary>How many units of this currency one US dollar buys (USD itself is 1).</summary>
    public required decimal RateToUsd { get; init; }

    public required bool IsDefault { get; init; }
}

public sealed record PosPaymentMethodDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required bool IsDefault { get; init; }

    /// <summary>The checkout dialog asks for the amount handed over and shows change.</summary>
    public required bool RequiresChange { get; init; }
}

public sealed record PosDiscountDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Percentage, 0-100.</summary>
    public required decimal Rate { get; init; }

    public required bool IsDefault { get; init; }

    public int? SupplierId { get; init; }
}

public sealed record PosCustomerDto
{
    public required int Id { get; init; }

    /// <summary>First and last name.</summary>
    public required string Name { get; init; }

    public string? Email { get; init; }

    /// <summary>What the customer owes (positive) or has in credit (negative).</summary>
    public required decimal Balance { get; init; }
}

/// <summary>One page of customers for the lazy-loaded pickers. There is no total: ask for the next page while <see cref="HasMore"/>.</summary>
public sealed record PosCustomerPageDto
{
    public required IReadOnlyList<PosCustomerDto> Items { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public required bool HasMore { get; init; }
}

public sealed class PosQuickCustomerRequest
{
    /// <summary>2-100 characters.</summary>
    [Required, StringLength(100, MinimumLength = 2)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>2-100 characters.</summary>
    [Required, StringLength(100, MinimumLength = 2)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Optional: blank means none. Same rule as the Customers page (up to 20 characters, phone-number shaped), checked after trimming.</summary>
    public string? PhoneNumber { get; set; }
}

/// <summary>The signed-in user's cash drawer, for the POS "open your drawer first" prompt.</summary>
public sealed record PosDrawerDto
{
    public required bool IsOpen { get; init; }

    public int? SessionId { get; init; }

    public DateTime? OpenedAt { get; init; }

    public decimal OpeningCash { get; init; }
}

// ---- a cart, as sent to be priced / held / sold ---------------------------------------------------------------------

/// <summary>
/// One line to price or sell. A product appears at most once in a cart (the server keeps one line per product); the price is
/// never sent: pick a stored price with <see cref="PriceTierId"/>, type a <see cref="CustomPrice"/>, or send neither for the default.
/// </summary>
public sealed class PosLineInput
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    /// <summary>Whole pieces, at least 1. For a product sold by weight or volume send 1 and fill <see cref="WeightOrVolume"/>.</summary>
    [Range(1, 100000)]
    public int Quantity { get; set; } = 1;

    /// <summary>Amount in the product's base unit (kg or L). Replaces <see cref="Quantity"/> in the line total when set. Must be above zero.</summary>
    public decimal? WeightOrVolume { get; set; }

    /// <summary>One of the product's stored extra prices.</summary>
    public int? PriceTierId { get; set; }

    /// <summary>A unit price typed by the cashier, in the cart currency. Refused below the purchase price.</summary>
    public decimal? CustomPrice { get; set; }
}

public sealed class PosCartInput
{
    /// <summary>The currency every price and total is shown and sold in (usually the default currency).</summary>
    [Range(1, int.MaxValue)]
    public int CurrencyId { get; set; }

    [Required, MinLength(1), MaxLength(500)]
    public List<PosLineInput> Lines { get; set; } = new();

    /// <summary>Money off the subtotal, in the cart currency. Ignored when <see cref="DiscountPercentage"/> is set.</summary>
    public decimal? DiscountAmount { get; set; }

    /// <summary>Percentage off the subtotal, 0-100. Takes precedence over <see cref="DiscountAmount"/>.</summary>
    public decimal? DiscountPercentage { get; set; }

    /// <summary>What the receipt calls the discount (a stored discount's name, or empty). Never grants anything by itself.</summary>
    [StringLength(200)]
    public string? DiscountLabel { get; set; }
}

public sealed record PosPricedLineDto
{
    public required int ProductId { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }

    public decimal? WeightOrVolume { get; init; }

    /// <summary>The stored price chosen, if any.</summary>
    public int? PriceTierId { get; init; }

    /// <summary>The stored price's name; null for the default and for a custom price.</summary>
    public string? PriceLabel { get; init; }

    public required bool IsCustomPrice { get; init; }

    /// <summary>In the cart currency, rounded to 2 decimals.</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary><see cref="UnitPrice"/> x (<see cref="WeightOrVolume"/> or <see cref="Quantity"/>); not rounded.</summary>
    public required decimal LineTotal { get; init; }

    /// <summary>The lowest custom unit price for this product, in the cart currency.</summary>
    public required decimal MinUnitPrice { get; init; }

    /// <summary>On hand now, in the product's unit.</summary>
    public required decimal StockQuantity { get; init; }

    /// <summary>Selling this line would take stock below zero (checkout then needs <c>allowInsufficientStock</c>).</summary>
    public required bool InsufficientStock { get; init; }
}

/// <summary>The server's price for a cart. Show these numbers; send <see cref="Total"/> back as <c>totalAmount</c> at checkout.</summary>
public sealed record PosPricedCartDto
{
    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    public required IReadOnlyList<PosPricedLineDto> Lines { get; init; }

    /// <summary>Sum of the line totals.</summary>
    public required decimal Subtotal { get; init; }

    /// <summary>Money off, whichever way it was entered.</summary>
    public required decimal DiscountAmount { get; init; }

    /// <summary>The discount as a percentage of the subtotal.</summary>
    public required decimal DiscountPercentage { get; init; }

    /// <summary>Taxes are not applied at the till today (the receipt always carries 0), so this is always 0. Present so the shape does not change if that does.</summary>
    public required decimal TaxAmount { get; init; }

    /// <summary>Subtotal - discount, rounded to 2 decimals: what the customer pays.</summary>
    public required decimal Total { get; init; }

    /// <summary>Any line would sell below zero stock.</summary>
    public required bool HasInsufficientStock { get; init; }
}

// ---- checkout -------------------------------------------------------------------------------------------------------

public sealed class PosCheckoutRequest
{
    /// <summary>A fresh value (a GUID) per checkout attempt. Sending the same key again returns the same sale instead of a second one,
    /// so a retry after a lost response is safe. Reuse the key to retry; make a new one when the cart changed.</summary>
    [Required, StringLength(64, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required]
    public PosCartInput Cart { get; set; } = new();

    /// <summary>Null or 0 is a walk-in customer.</summary>
    public int? CustomerId { get; set; }

    /// <summary>Ignored when <see cref="AddToDebt"/> is true (the server resolves the system "Customer Debt" method itself).</summary>
    public int PaymentMethodId { get; set; }

    /// <summary>Put the sale on the customer's account instead of collecting payment. Needs a real customer.</summary>
    public bool AddToDebt { get; set; }

    /// <summary>What the customer pays, in the cart currency: the priced total, which the cashier may lower (a discount) or raise
    /// (cash rounding). No limit is enforced.</summary>
    [Range(0, 1_000_000_000)]
    public decimal TotalAmount { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>Print the receipt on the receipt printer (outcome in <c>receiptPrinted</c>).</summary>
    public bool PrintReceipt { get; set; } = true;

    /// <summary>Sell even if a product would go below zero stock (stock is floored at zero). Set after the cashier confirms an <c>insufficient_stock</c> warning.</summary>
    public bool AllowInsufficientStock { get; set; }

    /// <summary>Refuse the sale with 409 <c>drawer_not_open</c> while the cashier has no open cash drawer. Off by default: the old till sold anyway
    /// and the sale simply was not part of any drawer's Z report.</summary>
    public bool RequireOpenDrawer { get; set; }
}

public sealed record PosSaleLineDto
{
    public required int ProductId { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }

    public decimal? WeightOrVolume { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }
}

/// <summary>A completed sale: everything the success screen and a receipt preview need. The paper / PDF receipt stays server-side (<c>print</c> / <c>receipt.pdf</c>).</summary>
public sealed record PosSaleDto
{
    public required int OrderId { get; init; }

    public required string OrderNumber { get; init; }

    /// <summary>Null if the receipt row could not be created (the sale itself is saved; support sees it in the audit trail).</summary>
    public int? ReceiptId { get; init; }

    public string? ReceiptNumber { get; init; }

    public required DateTime OrderDate { get; init; }

    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    public required decimal Subtotal { get; init; }

    public required decimal DiscountAmount { get; init; }

    public required decimal TaxAmount { get; init; }

    public required decimal Total { get; init; }

    public required decimal Paid { get; init; }

    public required decimal Change { get; init; }

    public required string PaymentMethod { get; init; }

    public int? CustomerId { get; init; }

    public string? CustomerName { get; init; }

    public required bool AddedToDebt { get; init; }

    /// <summary>True: this response replays an earlier checkout with the same <c>idempotencyKey</c>; nothing was sold again.</summary>
    public required bool Replayed { get; init; }

    /// <summary>The drawer session the sale was recorded in; null when the cashier had no open drawer.</summary>
    public int? CashDrawerSessionId { get; init; }

    /// <summary>Whether the receipt reached the thermal printer. Null when printing was not requested.</summary>
    public bool? ReceiptPrinted { get; init; }

    public required IReadOnlyList<PosSaleLineDto> Lines { get; init; }
}

/// <summary>A sale in the "last sales" list.</summary>
public sealed record PosSaleSummaryDto
{
    public required int OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required int ReceiptId { get; init; }

    public required string ReceiptNumber { get; init; }

    public required DateTime OrderDate { get; init; }

    public required decimal Total { get; init; }

    public required string CurrencyNotation { get; init; }

    public string? CustomerName { get; init; }
}

/// <summary>Outcome of a print request. Printing never fails the request: offer <see cref="PdfUrl"/> when <see cref="Printed"/> is false.</summary>
public sealed record PosPrintResultDto
{
    public required bool Printed { get; init; }

    /// <summary>Localized message for the toast.</summary>
    public required string Message { get; init; }

    /// <summary>Relative URL of the PDF version (<c>GET</c>, same cookie).</summary>
    public required string PdfUrl { get; init; }
}
