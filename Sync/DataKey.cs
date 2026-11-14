using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sync;

/// <summary>
/// The shop's end-to-end encryption key: 256 random bits made on the till and never sent to the cloud. Whoever holds it can read the
/// shop's copy; the provider does not, so the provider cannot read it (or recover it) either. The owner keeps a printed
/// <see cref="ToRecoveryCode">recovery code</see>, which is this key written so a person can copy it by hand, with a checksum so a
/// typo is caught instead of silently producing a wrong key.
/// </summary>
public sealed class DataKey
{
    public const int Size = 32;
    private const int ChecksumBytes = 3;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; // RFC 4648 base32

    private readonly byte[] _key;

    private DataKey(byte[] key)
    {
        if (key.Length != Size) throw new ArgumentException($"A data key is {Size} bytes.", nameof(key));
        _key = key;
    }

    public static DataKey Generate() => new(RandomNumberGenerator.GetBytes(Size));

    public static DataKey FromBase64(string base64) => new(Convert.FromBase64String(base64));

    public string ToBase64() => Convert.ToBase64String(_key);

    public RowCipher Cipher() => new(_key);

    /// <summary>56 characters in groups of four ("ABCD-EFGH-..."): the key plus a 3-byte checksum, in base32.</summary>
    public string ToRecoveryCode()
    {
        var data = new byte[Size + ChecksumBytes];
        _key.CopyTo(data, 0);
        Checksum(_key).CopyTo(data, Size);
        var text = Base32(data);
        return string.Join('-', Enumerable.Range(0, text.Length / 4).Select(i => text.Substring(i * 4, 4)));
    }

    /// <summary>Accepts what a person types: any case, with or without dashes and spaces. False if it is not a valid code (typo, wrong length).</summary>
    public static bool TryParseRecoveryCode(string? code, out DataKey? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(code)) return false;

        var clean = new string(code.Where(c => c is not ('-' or ' ' or '\t' or '\r' or '\n')).ToArray()).ToUpperInvariant();
        if (clean.Length != (Size + ChecksumBytes) * 8 / 5) return false;
        var data = FromBase32(clean);
        if (data is null || data.Length != Size + ChecksumBytes) return false;

        var candidate = data[..Size];
        if (!CryptographicOperations.FixedTimeEquals(data.AsSpan(Size), Checksum(candidate))) return false;
        key = new DataKey(candidate);
        return true;
    }

    private static byte[] Checksum(byte[] key) => SHA256.HashData(key)[..ChecksumBytes];

    private static string Base32(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 8 / 5 + 1);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[]? FromBase32(string text)
    {
        var result = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            var v = Alphabet.IndexOf(c);
            if (v < 0) return null;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8) { result.Add((byte)((buffer >> (bits - 8)) & 255)); bits -= 8; }
            buffer &= (1 << bits) - 1;
        }
        return result.ToArray();
    }
}

/// <summary>
/// Encrypts one row for the cloud: AES-256-GCM, a fresh random nonce each time, and the table and row id bound in as authenticated
/// data, so the cloud cannot hand the phone a row from another place (or an older row of another table) without the check failing.
/// Wire form: <c>e1.</c> + base64(nonce 12 | ciphertext | tag 16).
/// </summary>
public sealed class RowCipher
{
    public const string Prefix = "e1.";
    private const int NonceSize = 12, TagSize = 16;

    private readonly byte[] _key;

    internal RowCipher(byte[] key) => _key = key;

    public string Encrypt(string table, long id, IReadOnlyDictionary<string, object?> values)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(values, SyncWire.Json);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Aad(table, id));

        var packed = new byte[NonceSize + cipher.Length + TagSize];
        nonce.CopyTo(packed, 0);
        cipher.CopyTo(packed, NonceSize);
        tag.CopyTo(packed, NonceSize + cipher.Length);
        return Prefix + Convert.ToBase64String(packed);
    }

    /// <summary>Throws <see cref="CryptographicException"/> when the key is wrong or the row was altered or moved.</summary>
    public Dictionary<string, object?> Decrypt(string table, long id, string payload)
    {
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal)) throw new CryptographicException("Unknown payload format.");
        var packed = Convert.FromBase64String(payload[Prefix.Length..]);
        if (packed.Length < NonceSize + TagSize) throw new CryptographicException("Payload too short.");

        var cipher = packed.AsSpan(NonceSize, packed.Length - NonceSize - TagSize);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Decrypt(packed.AsSpan(0, NonceSize), cipher, packed.AsSpan(packed.Length - TagSize), plain, Aad(table, id));

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(plain, SyncWire.Json)
               ?? throw new CryptographicException("Empty row.");
    }

    /// <summary>
    /// Encrypts any bytes with the same scheme, bound to a <paramref name="purpose"/> and <paramref name="context"/> instead of a table and
    /// row. The owner's phone seals the changes it sends this way (purpose <c>command</c>, context the kind of change) and the shop PC seals
    /// its answers (purpose <c>result</c>, context the command's id), so the cloud in between carries only ciphertext it cannot alter or
    /// pass off as something else.
    /// </summary>
    public string Seal(string purpose, string context, ReadOnlySpan<byte> plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes($"{purpose}\n{context}"));

        var packed = new byte[NonceSize + cipher.Length + TagSize];
        nonce.CopyTo(packed, 0);
        cipher.CopyTo(packed, NonceSize);
        tag.CopyTo(packed, NonceSize + cipher.Length);
        return Prefix + Convert.ToBase64String(packed);
    }

    /// <summary>Opens what <see cref="Seal"/> made. Throws <see cref="CryptographicException"/> for the wrong key, an altered payload, or a different purpose or context.</summary>
    public byte[] Open(string purpose, string context, string payload)
    {
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal)) throw new CryptographicException("Unknown payload format.");
        byte[] packed;
        try { packed = Convert.FromBase64String(payload[Prefix.Length..]); }
        catch (FormatException) { throw new CryptographicException("Not base64."); }
        if (packed.Length < NonceSize + TagSize) throw new CryptographicException("Payload too short.");

        var cipher = packed.AsSpan(NonceSize, packed.Length - NonceSize - TagSize);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Decrypt(packed.AsSpan(0, NonceSize), cipher, packed.AsSpan(packed.Length - TagSize), plain, Encoding.UTF8.GetBytes($"{purpose}\n{context}"));
        return plain;
    }

    private static byte[] Aad(string table, long id) => Encoding.UTF8.GetBytes($"{table}\n{id}");
}
