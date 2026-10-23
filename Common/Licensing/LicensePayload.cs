namespace Common.Licensing;

public enum LicenseKind : byte
{
    /// <summary>Perpetual license. <see cref="LicensePayload.ValidUntil"/> is the end of the updates plan, not an expiry.</summary>
    Full = 0,

    /// <summary>Time-limited key issued while a purchase is paid in installments. <see cref="LicensePayload.ValidUntil"/> is the expiry.</summary>
    Provisional = 1,

    /// <summary>Time-limited evaluation key. <see cref="LicensePayload.ValidUntil"/> is the expiry.</summary>
    Trial = 2
}

/// <summary>The signed content of a license key.</summary>
/// <param name="Kind">Full, Provisional or Trial.</param>
/// <param name="Machine">The PC this license is bound to.</param>
/// <param name="IssuedOn">Day the key was issued. A newer key replaces an older one.</param>
/// <param name="ValidUntil">Full: last day of the updates plan. Provisional/Trial: last day the license is valid.</param>
/// <param name="Serial">Random number that identifies this key in the issuer's log.</param>
/// <param name="Customer">Customer name, at most <see cref="LicenseCodec.MaxCustomerBytes"/> bytes of UTF-8.</param>
public sealed record LicensePayload(
    LicenseKind Kind,
    MachineId Machine,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    uint Serial,
    string Customer)
{
    /// <summary>True for Provisional and Trial keys: they expire and then stop the POS. Full keys never expire.</summary>
    public bool IsTimeLimited => Kind != LicenseKind.Full;

    public string SerialText => Serial.ToString("X8");
}
