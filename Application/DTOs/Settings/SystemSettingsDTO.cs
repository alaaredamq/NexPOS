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

    public class AppSettingsDTO
    {
        // General Settings
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyAddress { get; set; } = string.Empty;
        public string CompanyPhone { get; set; } = string.Empty;
        public string CompanyEmail { get; set; } = string.Empty;
        public string CompanyLogo { get; set; } = string.Empty;
        public string DefaultLanguage { get; set; } = "en";
        public string DefaultCurrency { get; set; } = "USD";
        public string TimeZone { get; set; } = "UTC";
        public string DateFormat { get; set; } = "MM/dd/yyyy";
        public string TimeFormat { get; set; } = "12h";

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

        // Backup Settings
        public bool EnableAutoBackup { get; set; } = false;
        public string BackupFrequency { get; set; } = "daily"; // daily, weekly, monthly
        public int BackupRetentionDays { get; set; } = 30;
        public string BackupLocation { get; set; } = string.Empty;

        // Notification Settings
        public bool EnableEmailNotifications { get; set; } = false;
        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public string SmtpUsername { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty;
        public bool SmtpUseSsl { get; set; } = true;
        public string NotificationEmail { get; set; } = string.Empty;

        // Tax Settings
        public bool EnableTaxCalculation { get; set; } = true;
        public decimal DefaultTaxRate { get; set; } = 0;
        public bool TaxInclusive { get; set; } = false;
        public string TaxLabel { get; set; } = "Tax";

        // Theme Settings
        public string PrimaryColor { get; set; } = "#008080";
        public string SecondaryColor { get; set; } = "#006666";
        public string Theme { get; set; } = "light"; // light, dark
        public bool EnableAnimations { get; set; } = true;
        public string SidebarStyle { get; set; } = "default"; // default, compact, icon-only
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
