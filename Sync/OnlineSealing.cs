namespace Sync;

/// <summary>Where a shop stands on private (sealed) Online. The Online screen shows a different step for each.</summary>
public static class SealingStates
{
    /// <summary>Readable, as Online worked before sealing. A connected shop in this state is offered the switch.</summary>
    public const string Off = "off";
    /// <summary>Sealing is wanted but the owner has not yet confirmed the recovery slip, so nothing is uploaded.</summary>
    public const string NeedsSlip = "needsSlip";
    /// <summary>Sealing was confirmed but this PC has no key (a restore on a new machine). Nothing is uploaded until the key is restored or replaced.</summary>
    public const string KeyMissing = "keyMissing";
    /// <summary>Uploading sealed.</summary>
    public const string On = "on";

    public static string Of(OnlineSettings settings, bool keyExists)
        => !settings.SealData ? Off
         : settings.SealConfirmedAtUtc is null ? NeedsSlip
         : !keyExists ? KeyMissing
         : On;
}

/// <summary>The key as the owner keeps it. The code <b>is</b> the key: whoever has it can read the shop's cloud copy.</summary>
public sealed record RecoverySlipInfo(string Code, IReadOnlyList<string> Groups, string KeyId);

public enum RestoreOutcome { Restored, InvalidCode, DifferentKeyHere }

/// <summary>Sealing cannot go ahead; <see cref="Code"/> is stable for the screen.</summary>
public sealed class OnlineSealingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class OnlineSealingCodes
{
    public const string NotConnected = "not_connected";
    public const string NoKey = "no_key";
}

public interface IOnlineSealing
{
    /// <summary>The recovery slip. Makes the key on first use. The caller must have checked the owner's PIN: this reveals the key.</summary>
    RecoverySlipInfo GetSlip();

    /// <summary>
    /// The owner kept the slip: sealing starts. The next upload is the whole shop again, sealed, which replaces the cloud's
    /// readable copy. Needs a connected shop and an existing key.
    /// </summary>
    /// <exception cref="OnlineSealingException"><see cref="OnlineSealingCodes.NotConnected"/> or <see cref="OnlineSealingCodes.NoKey"/>.</exception>
    void Confirm();

    /// <summary>Puts the key from a recovery slip on this PC (a restore on a new machine). The caller must have checked the owner's PIN.</summary>
    RestoreOutcome RestoreKey(string code);
}

public sealed class OnlineSealing(OnlineSettingsStore store, SealKeyStore keys, OnlineSignal signal, TimeProvider time) : IOnlineSealing
{
    public RecoverySlipInfo GetSlip()
    {
        var key = keys.ExportKey();
        return new RecoverySlipInfo(RecoveryCode.Format(key), RecoveryCode.Groups(key), new RowSealer(key).KeyId);
    }

    public void Confirm()
    {
        var settings = store.Current;
        if (!(settings.Enabled && settings.IsConfigured))
            throw new OnlineSealingException(OnlineSealingCodes.NotConnected, "Connect to NexPOS Online first.");
        if (!keys.Exists)
            throw new OnlineSealingException(OnlineSealingCodes.NoKey, "There is no key yet: show the recovery slip first.");

        store.Save(settings with { SealData = true, SealConfirmedAtUtc = time.GetUtcNow().UtcDateTime });
        signal.Wake();
    }

    public RestoreOutcome RestoreKey(string code)
    {
        if (!RecoveryCode.TryParse(code, out var key)) return RestoreOutcome.InvalidCode;
        try
        {
            keys.Import(key);
            signal.Wake();
            return RestoreOutcome.Restored;
        }
        catch (InvalidOperationException)
        {
            return RestoreOutcome.DifferentKeyHere;
        }
    }
}
