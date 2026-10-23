using System.Text.Json;
using Infrastructure.Data;

namespace Web.Services;

/// <summary>
/// White-label branding: a custom app name, logo and favicon. Anything not set falls back to
/// the built-in NexPos defaults. Kept in the per-user app-data folder (next to the database) as
/// plain files, so it needs no migration and survives reinstalls and updates.
/// </summary>
public interface IBrandingService
{
    string DefaultAppName { get; }
    string AppName { get; }
    bool HasCustomName { get; }
    bool HasCustomLogo { get; }
    bool HasCustomFavicon { get; }

    /// <summary>Changes whenever the branding changes; appended to image URLs to bust browser caches.</summary>
    string Version { get; }

    void SetAppName(string? name);
    void SaveLogo(Stream content, string extension);
    void SaveFavicon(Stream content, string extension);
    void RemoveLogo();
    void RemoveFavicon();

    /// <summary>Full path and content type of the custom file, or null when the default is in use.</summary>
    (string Path, string ContentType)? GetLogoFile();
    (string Path, string ContentType)? GetFaviconFile();
}

public class BrandingService : IBrandingService
{
    public const int MaxNameLength = 40;
    public const long MaxFileBytes = 2 * 1024 * 1024;

    public static readonly string[] LogoExtensions = { ".png", ".jpg", ".jpeg", ".svg", ".webp" };
    public static readonly string[] FaviconExtensions = { ".png", ".ico", ".svg" };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
    };

    private readonly string _folder;
    private readonly string _nameFile;
    private readonly object _gate = new();
    private string? _appName;

    public BrandingService()
    {
        _folder = Path.Combine(Path.GetDirectoryName(AppDataPathProvider.GetDatabaseFilePath())!, "branding");
        _nameFile = Path.Combine(_folder, "branding.json");
        _appName = ReadName();
    }

    public string DefaultAppName => "NexPos";
    public string AppName => _appName ?? DefaultAppName;
    public bool HasCustomName => _appName != null;
    public bool HasCustomLogo => FindFile("logo") != null;
    public bool HasCustomFavicon => FindFile("favicon") != null;

    public string Version
    {
        get
        {
            long stamp = 0;
            foreach (var file in new[] { _nameFile, FindFile("logo"), FindFile("favicon") })
            {
                if (file != null && File.Exists(file))
                    stamp = Math.Max(stamp, File.GetLastWriteTimeUtc(file).Ticks);
            }
            return stamp.ToString("x");
        }
    }

    public void SetAppName(string? name)
    {
        name = name?.Trim();
        lock (_gate)
        {
            if (string.IsNullOrEmpty(name) || name.Equals(DefaultAppName, StringComparison.Ordinal))
            {
                _appName = null;
                if (File.Exists(_nameFile)) File.Delete(_nameFile);
                return;
            }

            if (name.Length > MaxNameLength) name = name[..MaxNameLength];
            Directory.CreateDirectory(_folder);
            File.WriteAllText(_nameFile, JsonSerializer.Serialize(new BrandingFile { AppName = name }));
            _appName = name;
        }
    }

    public void SaveLogo(Stream content, string extension) => SaveImage("logo", content, extension, LogoExtensions);

    public void SaveFavicon(Stream content, string extension) => SaveImage("favicon", content, extension, FaviconExtensions);

    public void RemoveLogo() => RemoveImage("logo");

    public void RemoveFavicon() => RemoveImage("favicon");

    public (string Path, string ContentType)? GetLogoFile() => Describe(FindFile("logo"));

    public (string Path, string ContentType)? GetFaviconFile() => Describe(FindFile("favicon"));

    private void SaveImage(string kind, Stream content, string extension, string[] allowed)
    {
        extension = extension.ToLowerInvariant();
        if (!allowed.Contains(extension))
            throw new InvalidOperationException($"Unsupported file type '{extension}'. Allowed: {string.Join(", ", allowed)}");

        lock (_gate)
        {
            Directory.CreateDirectory(_folder);
            // Write to a temp file first so a failed or oversized upload never destroys the current image.
            var temp = Path.Combine(_folder, kind + ".tmp");
            using (var output = File.Create(temp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaxFileBytes)
                    {
                        output.Close();
                        File.Delete(temp);
                        throw new InvalidOperationException($"File is too large (max {MaxFileBytes / 1024 / 1024} MB).");
                    }
                    output.Write(buffer, 0, read);
                }
            }

            RemoveImage(kind);
            File.Move(temp, Path.Combine(_folder, kind + extension), overwrite: true);
        }
    }

    private void RemoveImage(string kind)
    {
        lock (_gate)
        {
            var existing = FindFile(kind);
            if (existing != null) File.Delete(existing);
        }
    }

    private string? FindFile(string kind)
    {
        if (!Directory.Exists(_folder)) return null;
        foreach (var ext in ContentTypes.Keys)
        {
            var candidate = Path.Combine(_folder, kind + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static (string Path, string ContentType)? Describe(string? path) =>
        path == null ? null : (path, ContentTypes[Path.GetExtension(path)]);

    private string? ReadName()
    {
        try
        {
            if (!File.Exists(_nameFile)) return null;
            var name = JsonSerializer.Deserialize<BrandingFile>(File.ReadAllText(_nameFile))?.AppName?.Trim();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch
        {
            return null; // unreadable file -> behave as if no custom name is set
        }
    }

    private class BrandingFile
    {
        public string? AppName { get; set; }
    }
}
