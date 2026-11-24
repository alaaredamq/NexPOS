using System.Net.Mail;
using System.Text.RegularExpressions;
using Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Services;

/// <summary>
/// What a phone login types to sign in: an email, a phone number or a username, whichever the owner prefers.
/// All three live in <see cref="OwnerUser.Email"/> (the column kept its name so no migration and no phone-app change were needed;
/// the sign-in API still calls the field "email"). Stored forms:
/// email = lower-case address; phone = digits only ("+961 3 123 456" is "9613123456"); username = lower-case letters, digits, . _ -.
/// </summary>
public static partial class OwnerSignIn
{
    public enum Kind { Email, Phone, Username }

    public const int MinPhoneDigits = 7, MaxPhoneDigits = 15, MinUsername = 3, MaxUsername = 40;

    public const string Rules =
        "Enter an email, a phone number, or a username (3 to 40 letters or digits; dot, dash and underscore allowed)";

    [GeneratedRegex(@"^\+?[\d\s\-().]+$")]
    private static partial Regex PhoneShape();

    [GeneratedRegex(@"^[a-z0-9._\-]+$")]
    private static partial Regex UsernameShape();

    /// <summary>The stored form of what was typed, or null when it is none of the three.</summary>
    public static (string Value, Kind Kind)? Normalize(string? typed)
    {
        var s = typed?.Trim() ?? "";
        if (s.Length == 0 || s.Length > 255) return null;

        if (s.Contains('@'))
        {
            var email = s.ToLowerInvariant();
            return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email ? (email, Kind.Email) : null;
        }

        if (PhoneShape().IsMatch(s))
        {
            var digits = new string(s.Where(char.IsAsciiDigit).ToArray());
            if (digits.StartsWith("00")) digits = digits[2..];  // 00961... is +961...
            return digits.Length is >= MinPhoneDigits and <= MaxPhoneDigits ? (digits, Kind.Phone) : null;
        }

        var name = s.ToLowerInvariant();
        return name.Length is >= MinUsername and <= MaxUsername && UsernameShape().IsMatch(name) && name.Any(char.IsAsciiLetter)
            ? (name, Kind.Username) : null;
    }

    /// <summary>A stored sign-in name that is a phone number (digits only; usernames always hold a letter).</summary>
    public static bool IsPhone(string stored) => stored.Length > 0 && stored.All(char.IsAsciiDigit);

    /// <summary>
    /// Whether two stored phone numbers are the same line written two ways: with or without the country code, with or without the
    /// local leading 0 ("03123456", "3123456" and "9613123456" all match). Leading zeros go, then one must end with the other.
    /// </summary>
    public static bool SamePhone(string a, string b)
    {
        a = a.TrimStart('0'); b = b.TrimStart('0');
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= MinPhoneDigits && longer.EndsWith(shorter, StringComparison.Ordinal);
    }

    /// <summary>
    /// The login this sign-in name belongs to, or null. Phones match loosely (see <see cref="SamePhone"/>), but only when exactly one
    /// login matches; <see cref="TakenAsync"/> keeps it that way when logins are made.
    /// </summary>
    public static async Task<OwnerUser?> FindAsync(IQueryable<OwnerUser> owners, string? typed)
    {
        if (Normalize(typed) is not { } name) return null;
        var (value, kind) = name;
        var exact = await owners.FirstOrDefaultAsync(u => u.Email == value);
        if (exact is not null || kind != Kind.Phone) return exact;

        var ids = (await PhoneLoginsAsync(owners)).Where(p => SamePhone(p.Email, value)).Select(p => p.Id).Take(2).ToList();
        return ids.Count == 1 ? await owners.FirstOrDefaultAsync(u => u.Id == ids[0]) : null;
    }

    /// <summary>Whether a new login with this stored sign-in name would clash with one that exists (on any client).</summary>
    public static async Task<bool> TakenAsync(IQueryable<OwnerUser> owners, string value, Kind kind)
    {
        if (await owners.AnyAsync(u => u.Email == value)) return true;
        return kind == Kind.Phone && (await PhoneLoginsAsync(owners)).Any(p => SamePhone(p.Email, value));
    }

    // Phone logins are few (one or two per shop), so they are compared in memory rather than in SQL.
    private static async Task<List<(int Id, string Email)>> PhoneLoginsAsync(IQueryable<OwnerUser> owners)
        => (await owners.Where(u => !u.Email.Contains("@")).Select(u => new { u.Id, u.Email }).ToListAsync())
            .Where(u => IsPhone(u.Email)).Select(u => (u.Id, u.Email)).ToList();
}
