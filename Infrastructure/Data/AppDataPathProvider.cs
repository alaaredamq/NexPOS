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
