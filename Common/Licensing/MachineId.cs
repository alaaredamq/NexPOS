using System.Security.Cryptography;
using System.Text;

namespace Common.Licensing;

/// <summary>
/// Identifies one PC without storing any raw hardware serial. It is four short salted hashes
/// ("slots"), one per hardware fact, so a license survives a single change (for example a
/// Windows reinstall or a replaced disk) but not a copy onto a different PC.
///
/// Slot order is part of the license format and must never change:
/// 0 = Windows MachineGuid, 1 = SMBIOS system UUID, 2 = motherboard serial, 3 = system disk serial.
/// A slot is all zeros when that fact was unavailable or a known placeholder value.
/// </summary>
public sealed class MachineId
{
    public const int SlotCount = 4;
    public const int BytesPerSlot = 3;
    public const int Length = SlotCount * BytesPerSlot;

    /// <summary>How many present slots must match. With fewer present slots, all of them must match.</summary>
    public const int RequiredMatches = 2;

    private static readonly HashSet<string> PlaceholderValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "n/a", "na", "null", "unknown", "default string", "default", "not specified",
        "not applicable", "to be filled by o.e.m.", "to be filled by oem", "system serial number",
        "system product name", "base board serial number", "o.e.m.", "oem", "invalid", "0", "123456789",
        "00000000-0000-0000-0000-000000000000",
        "ffffffff-ffff-ffff-ffff-ffffffffffff",
        "03000200-0400-0500-0006-000700080009",
        "12345678-1234-5678-90ab-cddeefaabbcc",
        "00020003-0004-0005-0006-000700080009"
    };

    private readonly byte[] _bytes;

    private MachineId(byte[] bytes) => _bytes = bytes;

    public ReadOnlySpan<byte> Bytes => _bytes;

    public bool IsEmpty => _bytes.All(b => b == 0);

    public static MachineId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
            throw new ArgumentException($"A machine id is exactly {Length} bytes.", nameof(bytes));
        return new MachineId(bytes.ToArray());
    }

    /// <summary>Builds the id from the raw hardware facts, in slot order. Null entries are treated as unavailable.</summary>
    public static MachineId FromComponents(IReadOnlyList<string?> components)
    {
        var bytes = new byte[Length];
        for (var slot = 0; slot < SlotCount; slot++)
        {
            var value = slot < components.Count ? components[slot] : null;
            if (!IsUsableComponent(value)) continue;

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"NexPOS|MID|1|{slot}|{value!.Trim().ToUpperInvariant()}"));
            var offset = slot * BytesPerSlot;
            Array.Copy(hash, 0, bytes, offset, BytesPerSlot);

            // All zeros means "unavailable", so a real hash that happens to be zero is nudged.
            if (bytes[offset] == 0 && bytes[offset + 1] == 0 && bytes[offset + 2] == 0)
                bytes[offset + 2] = 1;
        }
        return new MachineId(bytes);
    }

    /// <summary>False for empty values and for the placeholders cheap or refurbished PCs report (all zeros, "Default string", ...).</summary>
    public static bool IsUsableComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed.Length < 4) return false;
        if (PlaceholderValues.Contains(trimmed)) return false;
        // A run of one repeated character (0000..., FFFF...) is never a real serial.
        if (trimmed.Replace("-", "").Distinct().Count() == 1) return false;
        return true;
    }

    /// <summary>Parses the text form shown to users: 20 Base32 characters, dashes and spaces optional.</summary>
    public static bool TryParse(string? text, out MachineId id)
    {
        id = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) normalized.Append(char.ToUpperInvariant(c));
        }
        if (normalized.Length != 20) return false;
        if (!Base32.TryDecode(normalized.ToString(), out var bytes) || bytes.Length != Length) return false;

        id = new MachineId(bytes);
        return true;
    }

    /// <summary>Formats as XXXXX-XXXXX-XXXXX-XXXXX.</summary>
    public string Format()
    {
        var text = Base32.Encode(_bytes);
        return string.Join('-', Enumerable.Range(0, 4).Select(i => text.Substring(i * 5, 5)));
    }

    /// <summary>
    /// True when <paramref name="current"/> (this PC) is the PC this id was issued for: at least
    /// <see cref="RequiredMatches"/> of the slots that were present at issue time must match.
    /// </summary>
    public bool IsSameMachineAs(MachineId current)
    {
        var present = 0;
        var matched = 0;
        for (var slot = 0; slot < SlotCount; slot++)
        {
            var mine = _bytes.AsSpan(slot * BytesPerSlot, BytesPerSlot);
            if (mine.IndexOfAnyExcept((byte)0) < 0) continue; // unavailable when issued

            present++;
            if (mine.SequenceEqual(current._bytes.AsSpan(slot * BytesPerSlot, BytesPerSlot))) matched++;
        }

        return present > 0 && matched >= Math.Min(RequiredMatches, present);
    }

    public override string ToString() => Format();
}
