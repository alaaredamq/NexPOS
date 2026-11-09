using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sync;

/// <summary>
/// Till-side Online settings. Off by default: a till that never enrols behaves exactly as before and writes nothing extra.
/// </summary>
public sealed record OnlineSettings
{
    public const string SectionName = "Online";

    public bool Enabled { get; set; }
    /// <summary>Base address of the NexPOS Cloud site, e.g. https://cloud.example.com</summary>
    public string CloudUrl { get; set; } = "";
    /// <summary>Device token for this installation. Written by the enrolment flow; treat like a password.</summary>
    public string DeviceToken { get; set; } = "";
    public int IntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 500;
    /// <summary>How long the shop PC lets the cloud hold a request open waiting for a change from the owner's phone (seconds, 0 to 55).</summary>
    public int CommandWaitSeconds { get; set; } = 25;

    /// <summary>
    /// Seal what the cloud does not need before it leaves the shop (docs/online/PRIVACY-KEYS.md). It only takes effect once the
    /// owner has confirmed they kept the recovery slip (<see cref="SealConfirmedAtUtc"/>): a key nobody has a copy of would
    /// strand the cloud copy. Until then a till that wants sealing uploads nothing.
    /// </summary>
    public bool SealData { get; set; }

    /// <summary>When the owner confirmed they kept the recovery slip. Null until then; uploading (sealed) starts at that moment.</summary>
    public DateTime? SealConfirmedAtUtc { get; set; }

    /// <summary>
    /// Every new connection is sealed (<see cref="SealData"/> is switched on when the link is redeemed). There is no switch for
    /// this on the Online screen; the setting exists for the test rigs and for diagnosing a shop with support.
    /// </summary>
    public bool SealOnConnect { get; set; } = true;

    // Shown on the Online screen so the owner can see what they are connected to. Informational only.
    public string? SiteName { get; set; }
    public string? ClientName { get; set; }
    public DateTime? ConnectedAtUtc { get; set; }

    public bool IsConfigured => Uri.TryCreate(CloudUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(DeviceToken);
}

/// <summary>
/// Holds the settings and persists them to <c>online.json</c> next to the till's database. The file format is
/// <c>{ "Online": { ... } }</c>, the same as before the screen existed, so a hand-written file keeps working.
/// The file wins over the defaults from appsettings.json; a missing file means "not enrolled".
/// </summary>
public sealed class OnlineSettingsStore
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private OnlineSettings _current;

    public OnlineSettingsStore(string filePath, OnlineSettings defaults)
    {
        _path = filePath;
        _current = defaults;
        Load();
    }

    public OnlineSettings Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>Set when the file exists but could not be read; the settings then stay at their defaults (off).</summary>
    public string? LoadError { get; private set; }

    public void Save(OnlineSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var json = JsonSerializer.Serialize(new Dictionary<string, OnlineSettings> { [OnlineSettings.SectionName] = settings }, WriteOptions);

            // Write beside the target and swap, so a crash mid-write cannot leave a half file that switches Online off.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(_path)) File.Replace(temp, _path, destinationBackupFileName: null);
            else File.Move(temp, _path);

            _current = settings;
            LoadError = null;
        }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_path));
            var section = root?[OnlineSettings.SectionName];
            if (section is null) return;
            var loaded = section.Deserialize<OnlineSettings>(ReadOptions);
            if (loaded is not null) _current = loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadError = ex.Message;
        }
    }
}
