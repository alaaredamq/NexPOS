using System.Text.Json;

namespace Sync;

/// <summary>
/// The shop PC's memory of commands it has already run, kept next to <c>online.json</c>. If the PC applied a command but
/// could not tell the cloud (power cut, network drop), the cloud hands the command out again; the ledger answers with the
/// stored outcome instead of adding the same product twice. Holds the latest <see cref="Capacity"/> entries.
/// </summary>
public sealed class CommandLedger
{
    public const int Capacity = 500;

    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<KeyValuePair<Guid, CommandResult>> _entries = new();

    public CommandLedger(string filePath)
    {
        _path = filePath;
        Load();
    }

    public bool TryGet(Guid id, out CommandResult result)
    {
        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                if (entry.Key != id) continue;
                result = entry.Value;
                return true;
            }
        }
        result = null!;
        return false;
    }

    public void Record(Guid id, CommandResult result)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.Key == id);
            _entries.Add(new(id, result));
            if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var stored = JsonSerializer.Deserialize<Dictionary<Guid, CommandResult>>(File.ReadAllText(_path), SyncWire.Json);
            if (stored is not null) _entries.AddRange(stored);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable ledger only means a rare double-delivery could run twice; never block the shop over it.
            _entries.Clear();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries.ToDictionary(e => e.Key, e => e.Value), SyncWire.Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same reasoning as Load.
        }
    }
}
