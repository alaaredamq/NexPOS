namespace Core.Entities.Users;

/// <summary>
/// Whether two phone numbers are the same number, written differently: only the digits count, and only the last
/// <see cref="Significant"/> of them, so a country code (+961, 00961) or a leading 0 does not get in the way
/// ("+961 1 234 567" and "01 234 567"; "70-123456" and "0096170123456"). A number with fewer digits is too short to tell people apart
/// and never matches. Seven digits can, rarely, belong to two numbers: a match is a suggestion someone confirms, never acted on alone.
/// </summary>
public static class PhoneMatch
{
    public const int Significant = 7;

    /// <summary>The digits that identify the number, or null when there are too few to identify anyone.</summary>
    public static string? Key(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        return digits.Length < Significant ? null : digits[^Significant..];
    }

    public static bool Same(string? a, string? b) => Key(a) is { } ka && ka == Key(b);
}
