using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Cloud.Services;

/// <summary>PBKDF2-SHA256. Stored as <c>v1.iterations.salt.hash</c> so the cost can be raised later without a migration.</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Device tokens are 256 random bits; only their SHA-256 is stored, so a leaked control database cannot be used to impersonate a till.</summary>
public static class DeviceTokens
{
    public const string Scheme = "nxp_";

    public static (string Token, string Hash, string Prefix) Generate()
    {
        var token = Scheme + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token), token[..8]);
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>
/// Enrolment codes are meant to be read out, typed or pasted from a chat: 12 characters (60 bits) from an alphabet
/// without look-alikes (no 0/O, 1/I/L), shown as <c>XXXX-XXXX-XXXX</c>. Lookups ignore case, dashes and spaces.
/// </summary>
public static class EnrollmentCodes
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 12;

    public static (string Display, string Hash) Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        var raw = new string(chars);
        return ($"{raw[..4]}-{raw[4..8]}-{raw[8..]}", Hash(raw));
    }

    /// <summary>Upper-cases and strips everything that is not a letter or digit; null when what is left cannot be a code.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var cleaned = new string(input.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return cleaned.Length == Length && cleaned.All(c => Alphabet.Contains(c)) ? cleaned : null;
    }

    public static string Hash(string normalized) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("enroll:" + normalized)));
}

/// <summary>
/// Brute-force brake for the admin login: after a few failures from one address the address is refused for a
/// while, whatever password it tries next. In memory on purpose; a restart clears it, which is acceptable for a
/// single-admin portal.
/// </summary>
public sealed class LoginThrottle(TimeProvider time)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int Failures, DateTime FirstFailure)> _failures = new();

    public bool IsBlocked(string key)
    {
        if (!_failures.TryGetValue(key, out var entry)) return false;
        if (time.GetUtcNow().UtcDateTime - entry.FirstFailure > Window)
        {
            _failures.TryRemove(key, out _);
            return false;
        }
        return entry.Failures >= MaxFailures;
    }

    public void RecordFailure(string key)
    {
        var now = time.GetUtcNow().UtcDateTime;
        _failures.AddOrUpdate(key, (1, now),
            (_, old) => now - old.FirstFailure > Window ? (1, now) : (old.Failures + 1, old.FirstFailure));
    }

    public void Reset(string key) => _failures.TryRemove(key, out _);
}
