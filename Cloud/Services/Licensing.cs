using System.Globalization;
using System.Security.Cryptography;
using Cloud.Data;
using Common.Licensing;
using Microsoft.Extensions.Options;

namespace Cloud.Services;

/// <summary>Why the portal can or cannot issue keys right now.</summary>
public enum SignerState
{
    /// <summary>Cloud:LicenseSigningKeyPath is empty: online issuing is off.</summary>
    Off,
    /// <summary>The path is set but there is no file there.</summary>
    Missing,
    /// <summary>The file is a plain private key. Refused: anyone with the server's files could make licences.</summary>
    NotProtected,
    /// <summary>The file is not a private key the issuer could have written.</summary>
    Unreadable,
    Ready,
}

public sealed class LicenseSignException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Signs licence keys on the server with the same private key and codec as tools/LicenseIssuer, so keys from either are the same.
/// The key file must be passphrase-protected; the passphrase comes with each request, the key is decrypted for that one signature
/// and disposed, and nothing decrypted is kept. Wrong passphrases are throttled like sign-ins.
/// </summary>
public sealed class LicenseSigner(IOptions<CloudOptions> options, LoginThrottle throttle)
{
    private string Path => options.Value.LicenseSigningKeyPath?.Trim() ?? "";

    public SignerState State
    {
        get
        {
            if (Path.Length == 0) return SignerState.Off;
            if (!File.Exists(Path)) return SignerState.Missing;
            string pem;
            try { pem = File.ReadAllText(Path); }
            catch (IOException) { return SignerState.Unreadable; }
            catch (UnauthorizedAccessException) { return SignerState.Unreadable; }
            if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal)) return SignerState.Ready;
            return pem.Contains("PRIVATE KEY", StringComparison.Ordinal) ? SignerState.NotProtected : SignerState.Unreadable;
        }
    }

    public static string Explain(SignerState state) => state switch
    {
        SignerState.Off => "Issuing keys here is off. Put the passphrase-protected private.pem on the server and set Cloud:LicenseSigningKeyPath to it.",
        SignerState.Missing => "Cloud:LicenseSigningKeyPath is set, but there is no file there.",
        SignerState.NotProtected => "The signing key on the server has no passphrase, so it is not used: anyone who copied the server's files could make licences. Run 'LicenseIssuer protect' on it and upload it again.",
        SignerState.Unreadable => "The file at Cloud:LicenseSigningKeyPath is not a private key the issuer wrote.",
        _ => "Ready. You type the key's passphrase each time you issue; it is never stored.",
    };

    /// <summary>Signs the payload. Throws <see cref="LicenseSignException"/> with a code an app can branch on.</summary>
    public string Sign(LicensePayload payload, string? passphrase, string who)
    {
        var state = State;
        if (state != SignerState.Ready) throw new LicenseSignException("signer_" + state.ToString().ToLowerInvariant(), Explain(state));

        var throttleKey = "sign:" + who.ToLowerInvariant();
        if (throttle.IsBlocked(throttleKey))
            throw new LicenseSignException("rate_limited", "Too many wrong passphrases. Try again in a few minutes.");
        if (string.IsNullOrEmpty(passphrase)) throw new LicenseSignException("passphrase_required", "Enter the signing key's passphrase.");

        using var key = ECDsa.Create();
        try
        {
            key.ImportFromEncryptedPem(File.ReadAllText(Path), passphrase);
        }
        catch (CryptographicException)
        {
            throttle.RecordFailure(throttleKey);
            throw new LicenseSignException("wrong_passphrase", "Wrong passphrase for the signing key.");
        }
        throttle.Reset(throttleKey);

        var text = LicenseCodec.Encode(payload, key);
        // The classic mistake: a key pair the shipped app does not trust. Every shop PC would reject the key.
        byte[] trusted;
        try { trusted = Convert.FromBase64String(options.Value.LicensePublicKey); }
        catch (FormatException) { trusted = []; }
        if (trusted.Length == 0 || !LicenseCodec.Decode(text, trusted).Success)
            throw new LicenseSignException("key_mismatch", "The signing key on the server does not match the public key the app trusts. Keys from it would be rejected.");
        return text;
    }
}

/// <summary>Where a licence stands, as the provider cares about it (earlier than the shop PC's own warnings).</summary>
public enum LicenseStatus
{
    /// <summary>A newer key was issued for the same PC.</summary>
    Superseded,
    /// <summary>Time-limited, more than 14 days left.</summary>
    Active,
    /// <summary>Time-limited, ends within 14 days.</summary>
    EndingSoon,
    /// <summary>Time-limited, ended up to 7 days ago: the shop still sells (grace week).</summary>
    Grace,
    /// <summary>Time-limited, ended more than 7 days ago: the shop cannot sell.</summary>
    Expired,
    /// <summary>Full key, updates plan more than 30 days from ending.</summary>
    Updates,
    /// <summary>Full key, updates plan ends within 30 days: time to sell a renewal.</summary>
    UpdatesEnding,
    /// <summary>Full key, updates plan over. The POS keeps working; it just gets no new versions.</summary>
    UpdatesEnded,
}

public static class LicenseRules
{
    public const int EndingSoonDays = 14;
    public const int UpdatesEndingDays = 30;
    /// <summary>Same as the shop PC (LicenseOptions.GraceDays).</summary>
    public const int GraceDays = 7;

    public static bool IsTimeLimited(License l) => l.Kind != nameof(LicenseKind.Full);

    public static LicenseStatus Status(License l, DateOnly today, bool superseded)
    {
        if (superseded) return LicenseStatus.Superseded;
        var daysLeft = l.ValidUntil.DayNumber - today.DayNumber;
        if (!IsTimeLimited(l))
            return daysLeft < 0 ? LicenseStatus.UpdatesEnded : daysLeft <= UpdatesEndingDays ? LicenseStatus.UpdatesEnding : LicenseStatus.Updates;
        if (daysLeft < -GraceDays) return LicenseStatus.Expired;
        if (daysLeft < 0) return LicenseStatus.Grace;
        return daysLeft <= EndingSoonDays ? LicenseStatus.EndingSoon : LicenseStatus.Active;
    }

    public static (string Tone, string Label) Pill(LicenseStatus s) => s switch
    {
        LicenseStatus.Active => ("ok", "Active"),
        LicenseStatus.EndingSoon => ("warn", "Ending soon"),
        LicenseStatus.Grace => ("bad", "Grace week"),
        LicenseStatus.Expired => ("bad", "Expired"),
        LicenseStatus.Updates => ("ok", "Updates on"),
        LicenseStatus.UpdatesEnding => ("warn", "Updates ending"),
        LicenseStatus.UpdatesEnded => ("none", "Updates ended"),
        _ => ("none", "Replaced"),
    };

    /// <summary>
    /// The licences that are no longer the one in force on their PC: the shop PC keeps the newest key (and on the same day a Full key
    /// beats a time-limited one), so every older key for the same Machine ID is superseded.
    /// </summary>
    public static HashSet<int> Superseded(IEnumerable<License> all)
    {
        var result = new HashSet<int>();
        foreach (var group in all.GroupBy(l => l.MachineId))
        {
            var current = group.OrderByDescending(l => l.IssuedOn).ThenBy(l => IsTimeLimited(l) ? 1 : 0).ThenByDescending(l => l.Id).First();
            foreach (var l in group) if (l.Id != current.Id) result.Add(l.Id);
        }
        return result;
    }

    public static string Format(MachineId machine) => machine.Format();

    public static string Describe(License l)
        => $"{l.Plan} · {l.Kind}{(l.Seats > 1 ? $" · {l.Seats} PCs" : "")} · " +
           (IsTimeLimited(l) ? $"ends {l.ValidUntil.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : $"updates until {l.ValidUntil.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
}
