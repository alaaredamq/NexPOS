namespace Common.Licensing;

/// <summary>
/// The issuer's PUBLIC key (ECDSA P-256, SubjectPublicKeyInfo, Base64). Safe to ship and to commit.
/// The matching private key is created by `LicenseIssuer keygen` and must never be committed.
/// </summary>
public static class LicensePublicKey
{
    public const string SpkiBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEk2GRX6oebV5JD86cuSJ0hJ1+EAPYZmAqSfQE3PRwh3wpfnrClXtWrka7NYs0hy/EfeAqIRW2Rp7jb0MG2twqBA==";
}
