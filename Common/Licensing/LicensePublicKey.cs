namespace Common.Licensing;

/// <summary>
/// The issuer's PUBLIC key (ECDSA P-256, SubjectPublicKeyInfo, Base64). Safe to ship and to commit.
/// The matching private key is created by `LicenseIssuer keygen` and must never be committed.
/// </summary>
public static class LicensePublicKey
{
    public const string SpkiBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEwM8HC/mx6Jvg1qmZbo85uJPdeyimUDxB2OBZUCrTWDlmjsNlU2Vvw7A8UVqlLldk9heMnm5wBc3bsijT3RlY/Q==";
}
