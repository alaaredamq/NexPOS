using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Common.Licensing;

public enum LicenseDecodeError
{
    None,
    /// <summary>Not a license key, truncated, or has extra characters.</summary>
    Malformed,
    /// <summary>A key from a newer key format than this build understands.</summary>
    UnsupportedVersion,
    /// <summary>Well-formed, but not signed by the issuer's private key (or edited after signing).</summary>
    BadSignature
}

public sealed record LicenseDecodeResult(bool Success, LicensePayload? Payload, LicenseDecodeError Error)
{
    public static LicenseDecodeResult Ok(LicensePayload payload) => new(true, payload, LicenseDecodeError.None);
    public static LicenseDecodeResult Fail(LicenseDecodeError error) => new(false, null, error);
}

/// <summary>
/// Reads and writes license keys. A key is Base32(payload || signature):
/// the payload is a small binary record and the signature is ECDSA P-256 / SHA-256 over it.
/// Only the issuer holds the private key; the app embeds the public key and can verify offline.
///
/// Payload layout (big-endian):
/// [0] format version (1) | [1] kind | [2..13] machine id | [14..15] issued day | [16..17] valid-until day |
/// [18..21] serial | [22] name length N | [23..23+N) customer name, UTF-8. Days count from 2024-01-01.
///
/// Version 2 is the same record plus one trailing byte after the name: the seat count (PCs covered, 1..255).
/// A key for a single seat is still written as version 1, so builds that predate seats keep accepting it; only
/// multi-seat keys need a build that knows version 2 (an older build says "unsupported version" rather than
/// quietly treating a 5-PC package as one PC).
/// </summary>
public static class LicenseCodec
{
    /// <summary>Human-readable marker at the start of every key text ("NexPOS License v1").</summary>
    public const string Prefix = "NXL1";

    public const int MaxCustomerBytes = 60;

    public const int MaxSeats = 255;

    private const byte FormatVersion = 1;
    private const byte FormatVersionWithSeats = 2;
    private const int SignatureLength = 64;
    private const int FixedPayloadLength = 23;
    private static readonly DateOnly Epoch = new(2024, 1, 1);

    public static ECDsa CreatePrivateKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static string ExportPublicKeyBase64(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>Signs <paramref name="payload"/> and returns the key text, e.g. NXL1-ABCDE-FGHIJ-...</summary>
    public static string Encode(LicensePayload payload, ECDsa privateKey)
    {
        var payloadBytes = SerializePayload(payload);
        var signature = privateKey.SignData(payloadBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var raw = new byte[payloadBytes.Length + signature.Length];
        payloadBytes.CopyTo(raw, 0);
        signature.CopyTo(raw, payloadBytes.Length);

        return Format(raw);
    }

    /// <summary>Parses and verifies key text. Tolerates whitespace, dashes, line breaks and lower case.</summary>
    public static LicenseDecodeResult Decode(string? text, byte[] publicKeySpki)
    {
        if (string.IsNullOrWhiteSpace(text)) return LicenseDecodeResult.Fail(LicenseDecodeError.Malformed);

        var normalized = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            // Letters and digits only: drops dashes, spaces, line breaks and the invisible
            // characters chat apps sometimes add when a key is copied.
            if (char.IsLetterOrDigit(c)) normalized.Append(char.ToUpperInvariant(c));
        }

        var body = normalized.ToString();
        if (!body.StartsWith(Prefix, StringComparison.Ordinal)) return LicenseDecodeResult.Fail(LicenseDecodeError.Malformed);
        body = body[Prefix.Length..];

        if (!Base32.TryDecode(body, out var raw) || raw.Length < FixedPayloadLength + SignatureLength)
            return LicenseDecodeResult.Fail(LicenseDecodeError.Malformed);

        var version = raw[0];
        if (version != FormatVersion && version != FormatVersionWithSeats)
            return LicenseDecodeResult.Fail(LicenseDecodeError.UnsupportedVersion);

        var payloadLength = FixedPayloadLength + raw[22] + (version == FormatVersionWithSeats ? 1 : 0);
        if (raw.Length != payloadLength + SignatureLength) return LicenseDecodeResult.Fail(LicenseDecodeError.Malformed);

        var payloadBytes = raw.AsSpan(0, payloadLength);
        var signature = raw.AsSpan(payloadLength, SignatureLength);

        try
        {
            using var publicKey = ECDsa.Create();
            publicKey.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
            if (!publicKey.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return LicenseDecodeResult.Fail(LicenseDecodeError.BadSignature);
        }
        catch (CryptographicException)
        {
            return LicenseDecodeResult.Fail(LicenseDecodeError.BadSignature);
        }

        // The signature is valid, so the bytes are exactly what the issuer wrote.
        var kind = (LicenseKind)raw[1];
        if (!Enum.IsDefined(kind)) return LicenseDecodeResult.Fail(LicenseDecodeError.Malformed);

        var seats = 1;
        if (version == FormatVersionWithSeats)
        {
            seats = payloadBytes[^1];
            if (seats < 1) return LicenseDecodeResult.Fail(LicenseDecodeError.Malformed);
        }

        var payload = new LicensePayload(
            kind,
            MachineId.FromBytes(payloadBytes.Slice(2, MachineId.Length)),
            FromDay(BinaryPrimitives.ReadUInt16BigEndian(payloadBytes.Slice(14, 2))),
            FromDay(BinaryPrimitives.ReadUInt16BigEndian(payloadBytes.Slice(16, 2))),
            BinaryPrimitives.ReadUInt32BigEndian(payloadBytes.Slice(18, 4)),
            Encoding.UTF8.GetString(payloadBytes.Slice(FixedPayloadLength, raw[22])),
            seats);

        return LicenseDecodeResult.Ok(payload);
    }

    /// <summary>Re-formats a decoded key into the canonical text, so what is stored never depends on how it was pasted.</summary>
    public static string Canonicalize(string text)
    {
        var normalized = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) normalized.Append(char.ToUpperInvariant(c));
        }
        var body = normalized.ToString();
        if (body.StartsWith(Prefix, StringComparison.Ordinal)) body = body[Prefix.Length..];
        return Group(body);
    }

    private static string Format(byte[] raw) => Group(Base32.Encode(raw));

    private static string Group(string base32Body)
    {
        var sb = new StringBuilder(Prefix);
        for (var i = 0; i < base32Body.Length; i += 5)
        {
            sb.Append('-').Append(base32Body, i, Math.Min(5, base32Body.Length - i));
        }
        return sb.ToString();
    }

    private static byte[] SerializePayload(LicensePayload payload)
    {
        var name = TruncateUtf8(payload.Customer?.Trim() ?? string.Empty, MaxCustomerBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);

        if (payload.Machine.IsEmpty)
            throw new ArgumentException("A license cannot be issued for an empty machine id.", nameof(payload));

        if (payload.Seats is < 1 or > MaxSeats)
            throw new ArgumentOutOfRangeException(nameof(payload), $"A license covers between 1 and {MaxSeats} PCs.");

        var withSeats = payload.Seats > 1;
        var bytes = new byte[FixedPayloadLength + nameBytes.Length + (withSeats ? 1 : 0)];
        bytes[0] = withSeats ? FormatVersionWithSeats : FormatVersion;
        bytes[1] = (byte)payload.Kind;
        payload.Machine.Bytes.CopyTo(bytes.AsSpan(2, MachineId.Length));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14, 2), ToDay(payload.IssuedOn));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16, 2), ToDay(payload.ValidUntil));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(18, 4), payload.Serial);
        bytes[22] = (byte)nameBytes.Length;
        nameBytes.CopyTo(bytes, FixedPayloadLength);
        if (withSeats) bytes[^1] = (byte)payload.Seats;
        return bytes;
    }

    private static ushort ToDay(DateOnly date)
    {
        var days = date.DayNumber - Epoch.DayNumber;
        if (days < 0 || days > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(date), $"License dates must be between {Epoch:yyyy-MM-dd} and {Epoch.AddDays(ushort.MaxValue):yyyy-MM-dd}.");
        return (ushort)days;
    }

    private static DateOnly FromDay(ushort day) => Epoch.AddDays(day);

    /// <summary>Cuts a string to at most <paramref name="maxBytes"/> of UTF-8 without splitting a character (Arabic names take 2 bytes per letter).</summary>
    private static string TruncateUtf8(string value, int maxBytes)
    {
        var sb = new StringBuilder();
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (used + size > maxBytes) break;
            sb.Append(rune.ToString());
            used += size;
        }
        return sb.ToString();
    }
}
