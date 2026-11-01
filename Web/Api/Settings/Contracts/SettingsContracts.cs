using System.ComponentModel.DataAnnotations;

namespace Web.Api.Settings.Contracts;

/// <summary>
/// The business details printed at the top and bottom of every receipt. These are the only store columns anything reads;
/// the rest of the table (email, currency symbol, logo path, ...) is not exposed and a save leaves it untouched.
/// </summary>
public sealed record StoreSettingsDto
{
    /// <summary>Printed as the receipt heading.</summary>
    public required string StoreName { get; init; }

    public required string AddressLine1 { get; init; }

    public required string AddressLine2 { get; init; }

    public required string Phone { get; init; }

    /// <summary>VAT / tax registration number.</summary>
    public required string TaxId { get; init; }

    public required string Website { get; init; }

    /// <summary>The "thank you" line, printed in capitals.</summary>
    public required string ReceiptFooterMessage { get; init; }

    public required string FooterLine1 { get; init; }

    public required string FooterLine2 { get; init; }

    public required string FooterLine3 { get; init; }
}

/// <summary>Body of "save the store details". Blank optional fields are stored as empty text.</summary>
public sealed record StoreSettingsRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string StoreName { get; init; }

    [StringLength(200)]
    public string? AddressLine1 { get; init; }

    [StringLength(200)]
    public string? AddressLine2 { get; init; }

    [StringLength(50)]
    public string? Phone { get; init; }

    [StringLength(50)]
    public string? TaxId { get; init; }

    [StringLength(200)]
    public string? Website { get; init; }

    [StringLength(500)]
    public string? ReceiptFooterMessage { get; init; }

    [StringLength(200)]
    public string? FooterLine1 { get; init; }

    [StringLength(200)]
    public string? FooterLine2 { get; init; }

    [StringLength(200)]
    public string? FooterLine3 { get; init; }
}

/// <summary>The shared printing settings: which printer prints receipts, and how big a barcode sticker is.</summary>
public sealed record PrintingSettingsDto
{
    /// <summary>Windows printer name; empty = receipts are not sent to a printer by the server.</summary>
    public required string ReceiptPrinterName { get; init; }

    /// <summary>Barcode sticker width in millimetres (15 - 150).</summary>
    public required int StickerWidthMm { get; init; }

    /// <summary>Barcode sticker height in millimetres (10 - 150).</summary>
    public required int StickerHeightMm { get; init; }
}

/// <summary>Body of "save the printing settings".</summary>
public sealed record PrintingSettingsRequest
{
    /// <summary>Empty clears the receipt printer. A printer that is not installed right now is accepted (it may just be unplugged).</summary>
    [StringLength(200)]
    public string? ReceiptPrinterName { get; init; }

    [Range(15, 150)]
    public required int StickerWidthMm { get; init; }

    [Range(10, 150)]
    public required int StickerHeightMm { get; init; }
}

/// <summary>Printers installed on the machine that runs the server (the till itself in the desktop app).</summary>
public sealed record InstalledPrintersDto
{
    public required IReadOnlyList<string> Items { get; init; }
}

/// <summary>Body of "print a test slip".</summary>
public sealed record PrinterTestRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string PrinterName { get; init; }
}

/// <summary>The outcome of a test print. A printer that cannot be reached is a normal answer (<c>printed</c> false), not an error.</summary>
public sealed record PrinterTestResultDto
{
    public required bool Printed { get; init; }

    /// <summary>Localized sentence for the user.</summary>
    public required string Message { get; init; }
}

/// <summary>White-label branding as the Branding page edits it. The images themselves are served by <c>/branding/logo</c> and <c>/branding/favicon</c>.</summary>
public sealed record BrandingSettingsDto
{
    /// <summary>The custom app name, or empty while the default is used.</summary>
    public required string AppName { get; init; }

    /// <summary>The name used when <see cref="AppName"/> is empty.</summary>
    public required string DefaultAppName { get; init; }

    public required bool HasCustomLogo { get; init; }

    public required bool HasCustomFavicon { get; init; }

    /// <summary>Cache-busted URL of the logo in use (the uploaded one or the built-in one).</summary>
    public required string LogoUrl { get; init; }

    /// <summary>Cache-busted URL of the favicon in use.</summary>
    public required string FaviconUrl { get; init; }

    public required int MaxNameLength { get; init; }

    public required long MaxFileBytes { get; init; }

    /// <summary>Allowed logo extensions, with the dot (<c>.png</c>).</summary>
    public required IReadOnlyList<string> LogoExtensions { get; init; }

    public required IReadOnlyList<string> FaviconExtensions { get; init; }
}

/// <summary>
/// Form of "save the branding" (<c>multipart/form-data</c>). Every part is optional: an absent <c>appName</c> or a blank one returns to the default
/// name, an absent file keeps the current image, <c>removeLogo</c> / <c>removeFavicon</c> return to the built-in image.
/// </summary>
public sealed class BrandingForm
{
    /// <summary>Blank or equal to the default name = use the default.</summary>
    public string? AppName { get; set; }

    /// <summary>PNG, JPG, SVG or WebP up to 2 MB.</summary>
    public IFormFile? Logo { get; set; }

    /// <summary>PNG, ICO or SVG up to 2 MB.</summary>
    public IFormFile? Favicon { get; set; }

    public bool RemoveLogo { get; set; }

    public bool RemoveFavicon { get; set; }
}
