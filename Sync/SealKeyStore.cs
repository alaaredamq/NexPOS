using System.Security.Cryptography;
using System.Text;

namespace Sync;

/// <summary>
/// The shop's seal key on disk, beside the database (<c>online.key</c>). On Windows it is protected with DPAPI for this
/// machine, so copying the file to another PC is useless. It is a separate file on purpose: backups snapshot the
/// database only, so a backup never carries the key. A restore on a new PC needs the recovery slip instead.
/// <para>
/// A damaged or unreadable key file is an error, never a reason to make a new key: a new key would silently strand
/// everything already sealed in the cloud.
/// </para>
/// </summary>
public sealed class SealKeyStore(string path)
{
    private const byte Raw = 0, Dpapi = 1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NexPOS.Online.SealKey.v1");
    private readonly object _gate = new();

    public string Path => path;

    public bool Exists => File.Exists(path);

    /// <summary>Loads the key, or makes and saves one the first time.</summary>
    public RowSealer LoadOrCreate()
    {
        lock (_gate) return new RowSealer(LoadOrCreateBytes());
    }

    /// <summary>Loads the key if there is one; null means none was ever made.</summary>
    public RowSealer? TryLoad()
    {
        lock (_gate) return File.Exists(path) ? new RowSealer(Read()) : null;
    }

    /// <summary>
    /// The raw key, made on first use. Only for the recovery slip and the phone link, behind the owner's PIN: whoever holds
    /// these bytes can read the shop's cloud copy.
    /// </summary>
    public byte[] ExportKey()
    {
        lock (_gate) return LoadOrCreateBytes();
    }

    /// <summary>
    /// Puts a key read from a recovery slip on this PC (a restore on a new machine). Refuses when this PC already has a
    /// different key: replacing it would strand what was sealed under it.
    /// </summary>
    /// <exception cref="InvalidOperationException">This PC already has a different key.</exception>
    public RowSealer Import(byte[] key)
    {
        var incoming = new RowSealer(key); // validates the length
        lock (_gate)
        {
            if (File.Exists(path))
            {
                var current = new RowSealer(Read());
                if (current.KeyId != incoming.KeyId)
                    throw new InvalidOperationException("This PC already has a different seal key.");
                return current;
            }
            Write(key);
            return incoming;
        }
    }

    private byte[] LoadOrCreateBytes()
    {
        if (File.Exists(path)) return Read();
        var key = RowSealer.NewKey();
        Write(key);
        return key;
    }

    private byte[] Read()
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 2) throw new InvalidDataException($"The seal key file {path} is damaged.");

        var payload = bytes[1..];
        switch (bytes[0])
        {
            case Raw:
                return payload;
            case Dpapi when OperatingSystem.IsWindows():
                try { return ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.LocalMachine); }
                catch (CryptographicException ex)
                {
                    throw new InvalidDataException($"The seal key file {path} cannot be opened on this PC (it was made on another machine, or is damaged).", ex);
                }
            default:
                throw new InvalidDataException($"The seal key file {path} cannot be opened on this system.");
        }
    }

    private void Write(byte[] key)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);

        byte[] file;
        if (OperatingSystem.IsWindows())
            file = [Dpapi, .. ProtectedData.Protect(key, Entropy, DataProtectionScope.LocalMachine)];
        else
            file = [Raw, .. key];

        // Write beside the target and swap, so a crash mid-write cannot leave half a key.
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, file);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, overwrite: false);
    }
}
