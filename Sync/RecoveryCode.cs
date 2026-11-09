using System.Security.Cryptography;
using System.Text;

namespace Sync;

/// <summary>
/// The seal key as something a person can keep on paper and type back: 14 groups of 4 letters and digits
/// (<c>K7QM-2XPA-…</c>), the last group being a check so a typing mistake is caught instead of making a wrong key.
/// The alphabet is base32 (A-Z, 2-7), which has no 0, 1, 8 or 9 to confuse with O, I or B; those are accepted
/// when typed and read as the letter. The code <b>is</b> the key: nothing is derived from a password, so there is nothing to guess.
/// </summary>
public static class RecoveryCode
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int KeyChars = 52;           // 256 bits at 5 bits a character (the last character carries 1 bit)
    private const int CheckChars = 4;
    private const int GroupSize = 4;

    /// <summary>The code as 14 groups of 4, the last one the check.</summary>
    public static string[] Groups(byte[] key)
    {
        if (key.Length != RowSealer.KeyLength) throw new ArgumentException($"A seal key is {RowSealer.KeyLength} bytes.", nameof(key));
        var text = Encode(key) + Check(key);
        return Enumerable.Range(0, text.Length / GroupSize).Select(i => text.Substring(i * GroupSize, GroupSize)).ToArray();
    }

    /// <summary>The code as one line, groups joined by dashes.</summary>
    public static string Format(byte[] key) => string.Join('-', Groups(key));

    /// <summary>Reads a code typed by a person: any case, spaces or dashes anywhere. False when it is not a whole, intact code.</summary>
    public static bool TryParse(string? text, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(text)) return false;

        var clean = new StringBuilder();
        foreach (var c in text.ToUpperInvariant())
        {
            if (c is ' ' or '-' or '\t' or '\r' or '\n') continue;
            clean.Append(c switch { '0' => 'O', '1' => 'I', '8' => 'B', _ => c });
        }
        if (clean.Length != KeyChars + CheckChars) return false;

        var body = clean.ToString(0, KeyChars);
        var check = clean.ToString(KeyChars, CheckChars);
        if (!Decode(body, out var decoded)) return false;
        if (!string.Equals(Check(decoded), check, StringComparison.Ordinal)) return false;

        key = decoded;
        return true;
    }

    private static string Encode(byte[] bytes)
    {
        var result = new StringBuilder(KeyChars);
        int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) result.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }

    private static bool Decode(string text, out byte[] bytes)
    {
        bytes = [];
        var result = new byte[RowSealer.KeyLength];
        int buffer = 0, bits = 0, index = 0;
        foreach (var c in text)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) return false;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                if (index >= result.Length) return false;
                result[index++] = (byte)((buffer >> (bits - 8)) & 0xFF);
                bits -= 8;
            }
            buffer &= (1 << bits) - 1;
        }
        // 52 characters carry 260 bits for a 256-bit key: the four spare bits must be zero, as the encoder leaves them.
        if (index != result.Length || buffer != 0) return false;
        bytes = result;
        return true;
    }

    /// <summary>First 20 bits of a hash of the key (domain-separated from the public key id), as 4 characters.</summary>
    private static string Check(byte[] key)
    {
        var hash = SHA256.HashData([.. Encoding.UTF8.GetBytes("NexPOS.RecoveryCode.check.v1"), .. key]);
        var value = (hash[0] << 12) | (hash[1] << 4) | (hash[2] >> 4);
        var chars = new char[CheckChars];
        for (var i = CheckChars - 1; i >= 0; i--, value >>= 5) chars[i] = Alphabet[value & 31];
        return new string(chars);
    }
}
