using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sync;

/// <summary>
/// Locks and opens one row with AES-256-GCM. The blob is <c>version | keyId | nonce | ciphertext | tag</c>, base64 on the wire.
/// <para>
/// The key id and the row's table and id are authenticated (not secret): the cloud cannot move a sealed row to another
/// table or id, or relabel it with another key id, without it failing to open. Each shop has its own key, so a row cannot
/// be moved to another shop either. A fresh random nonce is used for every row version.
/// </para>
/// </summary>
public sealed class RowSealer
{
    public const byte Version = 1;
    public const int KeyLength = 32;
    private const int KeyIdLength = 8, NonceLength = 12, TagLength = 16;
    private const int HeaderLength = 1 + KeyIdLength + NonceLength;

    private readonly byte[] _key;
    private readonly byte[] _keyId;

    public RowSealer(byte[] key)
    {
        if (key.Length != KeyLength) throw new ArgumentException($"A seal key is {KeyLength} bytes.", nameof(key));
        _key = (byte[])key.Clone();
        _keyId = SHA256.HashData(_key)[..KeyIdLength];
    }

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeyLength);

    /// <summary>Short public fingerprint of the key (16 hex characters). Safe to show and to store beside the data.</summary>
    public string KeyId => Convert.ToHexStringLower(_keyId);

    public string Seal(string table, long id, IReadOnlyDictionary<string, object?> values)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(values, SyncWire.Json);
        var blob = new byte[HeaderLength + plain.Length + TagLength];
        blob[0] = Version;
        _keyId.CopyTo(blob, 1);
        var nonce = blob.AsSpan(1 + KeyIdLength, NonceLength);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagLength);
        aes.Encrypt(nonce, plain, blob.AsSpan(HeaderLength, plain.Length), blob.AsSpan(HeaderLength + plain.Length, TagLength), Aad(_keyId, table, id));
        return Convert.ToBase64String(blob);
    }

    /// <exception cref="CryptographicException">Wrong key, wrong table or id, or the bytes were changed.</exception>
    public Dictionary<string, object?> Open(string table, long id, string sealedBase64)
    {
        if (!TryReadKeyId(sealedBase64, out var blobKeyId))
            throw new CryptographicException("Not a sealed row.");
        if (blobKeyId != KeyId)
            throw new CryptographicException("This row was sealed with a different key.");

        var blob = Convert.FromBase64String(sealedBase64);
        var cipherLength = blob.Length - HeaderLength - TagLength;
        var plain = new byte[cipherLength];

        using var aes = new AesGcm(_key, TagLength);
        aes.Decrypt(blob.AsSpan(1 + KeyIdLength, NonceLength), blob.AsSpan(HeaderLength, cipherLength),
            blob.AsSpan(HeaderLength + cipherLength, TagLength), plain, Aad(_keyId, table, id));

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(plain, SyncWire.Json)
               ?? throw new CryptographicException("Empty sealed row.");
    }

    /// <summary>
    /// Checks the shape of a sealed value without any key (the cloud has none) and reads its key id.
    /// </summary>
    public static bool TryReadKeyId(string sealedBase64, out string keyId)
    {
        keyId = "";
        byte[] blob;
        try { blob = Convert.FromBase64String(sealedBase64); }
        catch (FormatException) { return false; }

        if (blob.Length < HeaderLength + TagLength || blob[0] != Version) return false;
        keyId = Convert.ToHexStringLower(blob.AsSpan(1, KeyIdLength));
        return true;
    }

    private static byte[] Aad(byte[] keyId, string table, long id)
    {
        var label = Encoding.UTF8.GetBytes($"{table}|{id}");
        var aad = new byte[keyId.Length + label.Length];
        keyId.CopyTo(aad, 0);
        label.CopyTo(aad, keyId.Length);
        return aad;
    }
}
