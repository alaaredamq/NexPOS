using System.ComponentModel.DataAnnotations;

namespace Web.Api.Products.Contracts;

// Contract of track C1 (Products). Validation messages are keys of SharedResource, like the old DTOs: the API localizes them.

/// <summary>How much stock a product has, judged by the same rule the old list page used (see <see cref="ProductListSummaryDto.LowStockThreshold"/>).</summary>
public static class ProductStockLevel
{
    public const string Out = "out";
    public const string Low = "low";
    public const string Ok = "ok";
}

/// <summary>One row of the products list.</summary>
public sealed record ProductListItemDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string Barcode { get; init; }

    public required int CategoryId { get; init; }

    /// <summary>Null when the category row is gone; the UI shows its own "No category" text.</summary>
    public string? CategoryName { get; init; }

    public required int SupplierId { get; init; }

    public string? SupplierName { get; init; }

    public required int CurrencyId { get; init; }

    /// <summary>Short symbol of the product's currency (<c>$</c>, <c>LBP</c>) - what the old list printed in front of prices.</summary>
    public required string CurrencyNotation { get; init; }

    public required decimal PurchasePrice { get; init; }

    public required decimal SellingPrice { get; init; }

    public required decimal StockQuantity { get; init; }

    /// <summary><c>piece</c>, <c>kg</c>, <c>liter</c>, ... or null.</summary>
    public string? Unit { get; init; }

    public required bool IsActive { get; init; }

    public required bool IsPack { get; init; }

    /// <summary><c>out</c> (0), <c>low</c> (above 0 and up to the threshold) or <c>ok</c>.</summary>
    public required string StockLevel { get; init; }
}

/// <summary>The four tiles above the products list. Always counts the whole catalogue, whatever the list is filtered by.</summary>
public sealed record ProductListSummaryDto
{
    public required int Total { get; init; }

    public required int Active { get; init; }

    public required int LowStock { get; init; }

    public required int OutOfStock { get; init; }

    /// <summary>A product is "low" when its stock is above 0 and at most this many units.</summary>
    public required decimal LowStockThreshold { get; init; }
}

/// <summary>An extra price the cashier can pick at the till (Wholesale, ...). The selling price stays the default.</summary>
public sealed class ProductPriceTierDto
{
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

/// <summary>Everything about one product (details page and edit form).</summary>
public sealed record ProductDetailDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string Barcode { get; init; }

    public required int CategoryId { get; init; }

    public string? CategoryName { get; init; }

    public required int SupplierId { get; init; }

    public string? SupplierName { get; init; }

    /// <summary>The supplier's discount rate in percent (shown as a badge next to the supplier).</summary>
    public required double SupplierDiscountRate { get; init; }

    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    public required decimal PurchasePrice { get; init; }

    public required decimal SellingPrice { get; init; }

    /// <summary>Selling price minus purchase price.</summary>
    public required decimal ProfitMargin { get; init; }

    /// <summary>Profit as a percentage of the purchase price; 0 when there is no purchase price.</summary>
    public required decimal ProfitMarginPercent { get; init; }

    /// <summary>Selling price times stock on hand.</summary>
    public required decimal StockValue { get; init; }

    public required decimal StockQuantity { get; init; }

    public required decimal MinStockLevel { get; init; }

    public string? Unit { get; init; }

    public required bool IsActive { get; init; }

    public required bool IsPack { get; init; }

    public int? PackSize { get; init; }

    public int? SingleProductId { get; init; }

    /// <summary>Name of the single product a pack contains.</summary>
    public string? SingleProductName { get; init; }

    /// <summary>The extra prices in display order.</summary>
    public required IReadOnlyList<ProductPriceTierDto> PriceTiers { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public required string StockLevel { get; init; }
}

/// <summary>The body of create (POST) and update (PUT). Same rules as the old forms.</summary>
public sealed class ProductSaveRequest
{
    [Required(ErrorMessage = "Prod_NameRequired")]
    [StringLength(255, MinimumLength = 2, ErrorMessage = "Prod_NameLength")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>Must not be used by another product: a POS scan rings up whichever product the lookup finds first.</summary>
    [Required(ErrorMessage = "Barcode is required")]
    [StringLength(50, MinimumLength = 1)]
    public string Barcode { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Category is required")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Supplier is required")]
    public int SupplierId { get; set; }

    /// <summary>0 or omitted = the default currency.</summary>
    public int CurrencyId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Purchase price must be greater than 0")]
    public decimal PurchasePrice { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Selling price must be greater than 0")]
    public decimal SellingPrice { get; set; }

    [Range(0, double.MaxValue)]
    public decimal StockQuantity { get; set; }

    [Range(0, double.MaxValue)]
    public decimal MinStockLevel { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(50)]
    public string? Unit { get; set; }

    /// <summary>Create only - a product's pack setting cannot be changed afterwards (an update ignores the three pack fields).</summary>
    public bool IsPack { get; set; }

    /// <summary>Create only. How many single products one pack holds; required when <see cref="IsPack"/> is true.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Pack size must be greater than 0")]
    public int? PackSize { get; set; }

    /// <summary>Create only. The single product the pack contains; required when <see cref="IsPack"/> is true.</summary>
    public int? SingleProductId { get; set; }

    /// <summary>Extra prices. A fully blank row is ignored; otherwise it needs a name and an amount between the purchase price and infinity.</summary>
    public List<ProductPriceTierDto> PriceTiers { get; set; } = new();
}

/// <summary>The choices the product form and the list filters need, in one call.</summary>
public sealed record ProductLookupsDto
{
    /// <summary>Active categories.</summary>
    public required IReadOnlyList<ProductCategoryOption> Categories { get; init; }

    /// <summary>Active suppliers in their display order.</summary>
    public required IReadOnlyList<ProductSupplierOption> Suppliers { get; init; }

    public required IReadOnlyList<ProductCurrencyOption> Currencies { get; init; }

    /// <summary>The supplier a new product starts with: "No Supplier" if the shop has it, else the first one. Null if there are none.</summary>
    public int? DefaultSupplierId { get; init; }

    public int? DefaultCurrencyId { get; init; }
}

public sealed record ProductCategoryOption
{
    public required int Id { get; init; }

    public required string Name { get; init; }
}

public sealed record ProductSupplierOption
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Percent; shown next to the name when above 0.</summary>
    public required double DiscountRate { get; init; }
}

public sealed record ProductCurrencyOption
{
    public required int Id { get; init; }

    public required string Code { get; init; }

    public required string Notation { get; init; }

    public required bool IsDefault { get; init; }
}

/// <summary>A product in a picker (the single product of a pack).</summary>
public sealed record ProductOptionDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string Barcode { get; init; }
}

/// <summary>A freshly generated, unused EAN-13 barcode (in-store prefix <c>2</c> + date).</summary>
public sealed record GeneratedBarcodeDto
{
    public required string Barcode { get; init; }
}

/// <summary>The label size the shop chose in App Settings; the print dialog starts with it.</summary>
public sealed record StickerSettingsDto
{
    public required int WidthMm { get; init; }

    public required int HeightMm { get; init; }
}

/// <summary>
/// A barcode sticker as a PDF, for a plain browser (the desktop app prints to the label printer itself).
/// The browser encodes the barcode as bars (Code 128) and the server only draws them.
/// </summary>
public sealed class StickerPdfRequest
{
    public string? Name { get; set; }

    [Required]
    public string Code { get; set; } = string.Empty;

    /// <summary>The price as it should read on the label, already formatted.</summary>
    public string? Price { get; set; }

    /// <summary>The barcode as modules, <c>1</c> = bar and <c>0</c> = space.</summary>
    [Required]
    public string Bars { get; set; } = string.Empty;

    public int WidthMm { get; set; }

    public int HeightMm { get; set; }

    public int Copies { get; set; } = 1;

    public bool ShowPrice { get; set; } = true;
}

/// <summary>
/// A barcode sticker sent as label-printer commands (TSPL) straight to a printer on this computer. Same fields as the PDF,
/// plus the printer; the size travels in the commands, so it does not depend on the printer driver's paper size.
/// </summary>
public sealed class StickerPrintRequest
{
    public string? Name { get; set; }

    [Required]
    public string Code { get; set; } = string.Empty;

    /// <summary>The price as it should read on the label, already formatted.</summary>
    public string? Price { get; set; }

    /// <summary>The barcode as modules, <c>1</c> = bar and <c>0</c> = space.</summary>
    [Required]
    public string Bars { get; set; } = string.Empty;

    /// <summary>The Windows name of an installed label printer.</summary>
    [Required]
    public string PrinterName { get; set; } = string.Empty;

    public int WidthMm { get; set; }

    public int HeightMm { get; set; }

    public int Copies { get; set; } = 1;

    public bool ShowPrice { get; set; } = true;
}

/// <summary>The multipart body of the import: the workbook and whether to save it.</summary>
public sealed class ProductImportForm
{
    /// <summary>The Excel workbook (.xlsx) or a UTF-8 CSV, at most 10 MB.</summary>
    public IFormFile? File { get; set; }

    /// <summary>False = only check the file and report (the preview step). True = save the valid rows.</summary>
    public bool Apply { get; set; }
}

/// <summary>What an import did, or - for a preview - would do.</summary>
public sealed record ProductImportResultDto
{
    /// <summary>False for a preview: nothing was saved.</summary>
    public required bool Applied { get; init; }

    /// <summary>Set when the file as a whole is unusable (no header row, wrong encoding, ...). The counts are then 0.</summary>
    public string? FileError { get; init; }

    public required int TotalRows { get; init; }

    public required int Created { get; init; }

    public required int Updated { get; init; }

    public required int Unchanged { get; init; }

    /// <summary>Categories in the file that do not exist yet and are (or would be) created.</summary>
    public required int NewCategories { get; init; }

    /// <summary>Extra prices (Wholesale, ...) that are or would be saved from the Custom Prices sheet.</summary>
    public required int CustomPrices { get; init; }

    /// <summary>Rows of either sheet skipped because of an error.</summary>
    public required int Failed { get; init; }

    /// <summary>The first problems found (capped).</summary>
    public required IReadOnlyList<ProductImportIssueDto> Issues { get; init; }
}

public sealed record ProductImportIssueDto
{
    /// <summary>Row number as a spreadsheet shows it (the header is row 1).</summary>
    public required int Row { get; init; }

    public required string Message { get; init; }

    /// <summary>The sheet the row is on when it isn't the products sheet (e.g. "Custom Prices").</summary>
    public string? Sheet { get; init; }
}
