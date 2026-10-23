namespace Application.DTOs.Settings
{
    public class SystemSettingsDTO
    {
        public int Id { get; set; }
        public string SettingKey { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
        public string SettingType { get; set; } = string.Empty; // string, int, bool, decimal, json
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsEditable { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Every property here must be bound by an input on Settings/AppSettings.cshtml.
    /// UpdateAppSettingsAsync saves the *whole* DTO on every submit, so an unbound property
    /// would get silently reset to its C# default on the next save instead of preserving
    /// whatever was stored for it.
    /// </summary>
    public class AppSettingsDTO
    {
        // General Settings
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyAddress { get; set; } = string.Empty;
        public string CompanyPhone { get; set; } = string.Empty;
        public string CompanyEmail { get; set; } = string.Empty;
        public string DefaultLanguage { get; set; } = "en";
        public string DefaultCurrency { get; set; } = "USD";
        public string TimeZone { get; set; } = "UTC";

        // POS Settings
        public bool EnableBarcodeScanner { get; set; } = true;
        public bool EnableReceiptPrinting { get; set; } = true;
        public string ReceiptPrinterName { get; set; } = string.Empty;
        public bool AutoPrintReceipt { get; set; } = false;
        public bool ShowProductImages { get; set; } = true;
        public int ProductsPerPage { get; set; } = 30;
        public bool EnableDiscounts { get; set; } = true;
        public decimal MaxDiscountPercentage { get; set; } = 50;
        public bool RequireCustomerForSale { get; set; } = false;

        // Barcode sticker (label) size. The sticker *printer* is a per-machine choice kept in the
        // browser's localStorage (see wwwroot/js/sticker-label.js), not a shared setting.
        public int StickerWidthMm { get; set; } = 40;
        public int StickerHeightMm { get; set; } = 25;

        // Inventory Settings
        public bool EnableLowStockAlerts { get; set; } = true;
        public int LowStockThreshold { get; set; } = 10;
        public bool EnableNegativeStock { get; set; } = false;
        public bool AutoReorderEnabled { get; set; } = false;
        public int ReorderPoint { get; set; } = 5;
        public int ReorderQuantity { get; set; } = 50;

        // Security Settings
        public int SessionTimeout { get; set; } = 30; // minutes
        public int MaxLoginAttempts { get; set; } = 5;
        public int PasswordMinLength { get; set; } = 8;
        public bool RequireStrongPassword { get; set; } = true;
        public bool EnableTwoFactorAuth { get; set; } = false;
        public bool EnableAuditLogging { get; set; } = true;

        // Theme Settings
        public string PrimaryColor { get; set; } = "#008080";
        public string SecondaryColor { get; set; } = "#006666";
        public string Theme { get; set; } = "light"; // light, dark
        public bool EnableAnimations { get; set; } = true;
    }

    public class SettingUpdateRequest
    {
        public string SettingKey { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
    }

    public class BulkSettingUpdateRequest
    {
        public List<SettingUpdateRequest> Settings { get; set; } = new();
    }

    public class SettingCategoryDTO
    {
        public string CategoryName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<SystemSettingsDTO> Settings { get; set; } = new();
    }
}
