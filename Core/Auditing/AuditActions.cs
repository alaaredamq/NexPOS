namespace Core.Auditing;

/// <summary>
/// The action names used in the audit trail, in one place so the same thing is always spelled the
/// same way (the viewer filters on them).
/// </summary>
public static class AuditActions
{
    // Data
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string SoftDelete = "SoftDelete";
    public const string Restore = "Restore";
    public const string BulkInsert = "BulkInsert";
    public const string BulkUpdate = "BulkUpdate";
    public const string BulkDelete = "BulkDelete";
    public const string SaveFailed = "SaveFailed";

    // Security
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string PinChanged = "PinChanged";
    public const string PinChangeFailed = "PinChangeFailed";
    public const string PasswordChanged = "PasswordChanged";
    public const string AccessDenied = "AccessDenied";

    // Business
    public const string SaleCompleted = "SaleCompleted";
    public const string SaleFailed = "SaleFailed";
    public const string ReceiptFailed = "ReceiptFailed";
    public const string ReceiptVoided = "ReceiptVoided";
    public const string OrderCancelled = "OrderCancelled";
    public const string SaleCustomerChanged = "SaleCustomerChanged";
    public const string ProductsImported = "ProductsImported";
    public const string ProductsExported = "ProductsExported";
    public const string StockAdjusted = "StockAdjusted";
    public const string PurchaseReceived = "PurchaseReceived";
    public const string PurchaseCancelled = "PurchaseCancelled";
    public const string Print = "Print";
    public const string PrintFailed = "PrintFailed";
    public const string PdfGenerated = "PdfGenerated";

    // System
    public const string AppStarted = "AppStarted";
    public const string AppStopping = "AppStopping";
    public const string MigrationsApplied = "MigrationsApplied";
    public const string SeedData = "SeedData";
    public const string BackupCreated = "BackupCreated";
    public const string BackupFailed = "BackupFailed";
    public const string BackupRestored = "BackupRestored";
    public const string BackupRestoreFailed = "BackupRestoreFailed";
    public const string BackupDeleted = "BackupDeleted";
    public const string BackupCleanup = "BackupCleanup";
    public const string AuditPurged = "AuditPurged";
    public const string AuditRecovered = "AuditRecovered";
    public const string AuditExported = "AuditExported";

    // Diagnostic
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Critical = "Critical";
    public const string UnhandledException = "UnhandledException";

    // Entity-type names for events that aren't about a table
    public const string HttpEntity = "Http";
    public const string AuthEntity = "Auth";
}
