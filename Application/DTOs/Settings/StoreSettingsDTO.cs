namespace Application.DTOs.Settings;

/// <summary>
/// DTO for Store Settings
/// </summary>
public class StoreSettingsDTO
{
    public int Id { get; set; }
    
    /// <summary>
    /// Store/Business name displayed on receipts
    /// </summary>
    public string StoreName { get; set; } = "Your Store Name";

    /// <summary>
    /// Store address line 1 (street address)
    /// </summary>
    public string AddressLine1 { get; set; } = string.Empty;

    /// <summary>
    /// Store address line 2 (city, state, zip)
    /// </summary>
    public string AddressLine2 { get; set; } = string.Empty;

    /// <summary>
    /// Store phone number
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Tax ID / VAT number
    /// </summary>
    public string TaxId { get; set; } = string.Empty;

    /// <summary>
    /// Store email address
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Store website URL
    /// </summary>
    public string Website { get; set; } = string.Empty;

    /// <summary>
    /// Receipt footer message (thank you message)
    /// </summary>
    public string ReceiptFooterMessage { get; set; } = "Thank you for your purchase!";

    /// <summary>
    /// Additional footer line 1 (e.g., return policy)
    /// </summary>
    public string FooterLine1 { get; set; } = string.Empty;

    /// <summary>
    /// Additional footer line 2 (e.g., return policy details)
    /// </summary>
    public string FooterLine2 { get; set; } = string.Empty;

    /// <summary>
    /// Additional footer line 3 (optional)
    /// </summary>
    public string FooterLine3 { get; set; } = string.Empty;

    /// <summary>
    /// Currency symbol to use on receipts (e.g., $, €, £)
    /// </summary>
    public string CurrencySymbol { get; set; } = "$";

    /// <summary>
    /// Default language for receipts
    /// </summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>
    /// Whether to print store logo on receipts
    /// </summary>
    public bool PrintLogo { get; set; } = false;

    /// <summary>
    /// Path to store logo image
    /// </summary>
    public string LogoPath { get; set; } = string.Empty;

    /// <summary>
    /// Number of days for return policy
    /// </summary>
    public int ReturnPolicyDays { get; set; } = 30;

    /// <summary>
    /// Whether to show tax breakdown on receipts
    /// </summary>
    public bool ShowTaxBreakdown { get; set; } = true;

    /// <summary>
    /// Whether to print barcode on receipts
    /// </summary>
    public bool PrintBarcode { get; set; } = true;

    /// <summary>
    /// Social media handle (optional)
    /// </summary>
    public string SocialMedia { get; set; } = string.Empty;

    /// <summary>
    /// Business registration number
    /// </summary>
    public string BusinessRegistrationNumber { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for updating store settings
/// </summary>
public class UpdateStoreSettingsRequest
{
    public string StoreName { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string ReceiptFooterMessage { get; set; } = string.Empty;
    public string FooterLine1 { get; set; } = string.Empty;
    public string FooterLine2 { get; set; } = string.Empty;
    public string FooterLine3 { get; set; } = string.Empty;
    public string CurrencySymbol { get; set; } = "$";
    public string DefaultLanguage { get; set; } = "en";
    public bool PrintLogo { get; set; } = false;
    public string LogoPath { get; set; } = string.Empty;
    public int ReturnPolicyDays { get; set; } = 30;
    public bool ShowTaxBreakdown { get; set; } = true;
    public bool PrintBarcode { get; set; } = true;
    public string SocialMedia { get; set; } = string.Empty;
    public string BusinessRegistrationNumber { get; set; } = string.Empty;
}
