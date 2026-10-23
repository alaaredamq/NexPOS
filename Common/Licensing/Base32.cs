using System.Text;

namespace Common.Licensing;

/// <summary>
/// RFC 4648 Base32 without padding. Chosen for license keys because the alphabet has no
/// 0/1/8/9, so a key retyped by hand or read over the phone is far less error-prone than Base64.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
            buffer &= (1 << bits) - 1; // keep only the bits not written yet
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Decodes Base32 text. Case-insensitive. Also accepts 0, 1 and 8 as O, I and B, the
    /// characters people most often mistype them as. Any other character fails the decode.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<char> text, out byte[] data)
    {
        var bytes = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;

        foreach (var ch in text)
        {
            var value = ValueOf(ch);
            if (value < 0)
            {
                data = Array.Empty<byte>();
                return false;
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
                buffer &= (1 << bits) - 1;
            }
        }

        data = bytes.ToArray();
        return true;
    }

    private static int ValueOf(char ch)
    {
        if (ch >= 'A' && ch <= 'Z') return ch - 'A';
        if (ch >= 'a' && ch <= 'z') return ch - 'a';
        if (ch >= '2' && ch <= '7') return 26 + (ch - '2');
        return ch switch
        {
            '0' => 'O' - 'A',
            '1' => 'I' - 'A',
            '8' => 'B' - 'A',
            _ => -1
        };
    }
}
