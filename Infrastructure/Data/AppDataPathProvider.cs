namespace Infrastructure.Data;

/// <summary>
/// Resolves the SQLite database file to a per-user app-data folder so a packaged
/// (Electron) install never needs a separately-installed database server, and the
/// fixed install directory itself never has to be writable.
/// </summary>
public static class AppDataPathProvider
{
    /// <summary>Environment variable that overrides the database file location (used for demo/test databases).</summary>
    public const string DatabasePathEnvVar = "NEXPOS_DB_PATH";

    public static string GetDatabaseFilePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(DatabasePathEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var overrideFolder = Path.GetDirectoryName(Path.GetFullPath(overridePath));
            if (!string.IsNullOrEmpty(overrideFolder)) Directory.CreateDirectory(overrideFolder);
            return overridePath;
        }

        return Path.Combine(GetAppDataDirectory(), "nexpos.db");
    }

    public static string GetConnectionString() => $"Data Source={GetDatabaseFilePath()}";

    /// <summary>Environment variable that overrides the audit database location (development and testing only).</summary>
    public const string AuditDatabasePathEnvVar = "NEXPOS_AUDIT_DB_PATH";

    /// <summary>
    /// The audit trail's own SQLite file. When the business database is redirected (demo/test databases
    /// via <see cref="DatabasePathEnvVar"/>) the audit file follows it into the same folder, so a test
    /// run can never write into the real installation's audit history.
    /// </summary>
    public static string GetAuditDatabaseFilePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(AuditDatabasePathEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            EnsureFolder(overridePath);
            return overridePath;
        }

        var redirectedDb = Environment.GetEnvironmentVariable(DatabasePathEnvVar);
        if (!string.IsNullOrWhiteSpace(redirectedDb))
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(redirectedDb))!;
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, Path.GetFileNameWithoutExtension(redirectedDb) + "-audit.db");
        }

        return Path.Combine(GetAppDataDirectory(), "nexpos-audit.db");
    }

    public static string GetAuditConnectionString() => $"Data Source={GetAuditDatabaseFilePath()}";

    /// <summary>Where audit rows go if the audit database cannot be written (replayed on the next start).</summary>
    public static string GetAuditFallbackDirectory()
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(GetAuditDatabaseFilePath()))!, "audit-fallback");

    private static void EnsureFolder(string filePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
    }

    /// <summary>Environment variable that overrides the license file location (development and testing only).</summary>
    public const string LicensePathEnvVar = "NEXPOS_LICENSE_PATH";

    /// <summary>
    /// The license key file. Deliberately NOT derived from <see cref="DatabasePathEnvVar"/>: pointing the app
    /// at a demo/test database must not make it look for a license somewhere else.
    /// </summary>
    public static string GetLicenseFilePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(LicensePathEnvVar);
        return !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : Path.Combine(GetAppDataDirectory(), "license.nxl");
    }

    /// <summary>
    /// The Online (cloud mirror) settings file. Like the audit file it follows a redirected demo/test database into that
    /// database's folder, so a developer's real enrolment is never picked up by a demo run.
    /// </summary>
    public static string GetOnlineSettingsFilePath()
    {
        var redirectedDb = Environment.GetEnvironmentVariable(DatabasePathEnvVar);
        if (!string.IsNullOrWhiteSpace(redirectedDb))
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(redirectedDb))!;
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "online.json");
        }

        return Path.Combine(GetAppDataDirectory(), "online.json");
    }

    /// <summary>The per-user NexPOS folder (%LocalAppData%\NexPOS), created on first use.</summary>
    public static string GetAppDataDirectory()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexPOS");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
