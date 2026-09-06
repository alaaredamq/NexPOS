using Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace Core.Entities.Configuration;

/// <summary>
/// Store settings entity for receipt and store information.
/// This is a singleton entity - only one record should exist.
/// </summary>
public class StoreSettings : BaseEntity, IEntityBase<int>
{
    /// <summary>
    /// Store/Business name displayed on receipts
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string StoreName { get; set; } = "Your Store Name";

    /// <summary>
    /// Store address line 1 (street address)
    /// </summary>
    [MaxLength(200)]
    public string AddressLine1 { get; set; } = string.Empty;

    /// <summary>
    /// Store address line 2 (city, state, zip)
    /// </summary>
    [MaxLength(200)]
    public string AddressLine2 { get; set; } = string.Empty;

    /// <summary>
    /// Store phone number
    /// </summary>
    [MaxLength(50)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Tax ID / VAT number
    /// </summary>
    [MaxLength(50)]
    public string TaxId { get; set; } = string.Empty;

    /// <summary>
    /// Store email address
    /// </summary>
    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Store website URL
    /// </summary>
    [MaxLength(200)]
    public string Website { get; set; } = string.Empty;

    /// <summary>
    /// Receipt footer message (thank you message)
    /// </summary>
    [MaxLength(500)]
    public string ReceiptFooterMessage { get; set; } = "Thank you for your purchase!";

    /// <summary>
    /// Additional footer line 1 (e.g., return policy)
    /// </summary>
    [MaxLength(200)]
    public string FooterLine1 { get; set; } = "Please keep this receipt for your records";

    /// <summary>
    /// Additional footer line 2 (e.g., return policy details)
    /// </summary>
    [MaxLength(200)]
    public string FooterLine2 { get; set; } = "Returns accepted within 30 days";

    /// <summary>
    /// Additional footer line 3 (optional)
    /// </summary>
    [MaxLength(200)]
    public string FooterLine3 { get; set; } = string.Empty;

    /// <summary>
    /// Currency symbol to use on receipts (e.g., $, €, £)
    /// </summary>
    [MaxLength(10)]
    public string CurrencySymbol { get; set; } = "$";

    /// <summary>
    /// Default language for receipts
    /// </summary>
    [MaxLength(10)]
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>
    /// Whether to print store logo on receipts
    /// </summary>
    public bool PrintLogo { get; set; } = false;

    /// <summary>
    /// Path to store logo image
    /// </summary>
    [MaxLength(500)]
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
    [MaxLength(100)]
    public string SocialMedia { get; set; } = string.Empty;

    /// <summary>
    /// Business registration number
    /// </summary>
    [MaxLength(50)]
    public string BusinessRegistrationNumber { get; set; } = string.Empty;

    /// <summary>
    /// Last modified by user ID
    /// </summary>
    public int? LastModifiedByUserId { get; set; }

    /// <summary>
    /// Last modified date
    /// </summary>
    public DateTime? LastModifiedAt { get; set; }

    /// <summary>
    /// Creates a default StoreSettings instance
    /// </summary>
    public static StoreSettings CreateDefault()
    {
        return new StoreSettings
        {
            StoreName = "Your Store Name",
            AddressLine1 = "123 Main Street",
            AddressLine2 = "City, State 12345",
            Phone = "(123) 456-7890",
            TaxId = "123-456-7890",
            Email = "info@yourstore.com",
            Website = "www.yourstore.com",
            ReceiptFooterMessage = "Thank you for your purchase!",
            FooterLine1 = "Please keep this receipt for your records",
            FooterLine2 = "Returns accepted within 30 days",
            CurrencySymbol = "$",
            DefaultLanguage = "en",
            PrintLogo = false,
            ReturnPolicyDays = 30,
            ShowTaxBreakdown = true,
            PrintBarcode = true,
            CreatedAt = DateTime.UtcNow
        };
    }
}
