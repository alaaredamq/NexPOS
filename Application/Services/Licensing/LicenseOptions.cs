namespace Application.Services.Licensing
{
    public sealed class LicenseOptions
    {
        /// <summary>The issuer's public key (Base64 SubjectPublicKeyInfo) used to verify license keys.</summary>
        public required string PublicKeySpkiBase64 { get; init; }

        /// <summary>The day this build was compiled. A perpetual license only runs builds released on or before its updates-until date.</summary>
        public required DateOnly BuildDate { get; init; }

        /// <summary>False only in Debug builds, so development never needs a license.</summary>
        public bool EnforcementEnabled { get; init; } = true;

        /// <summary>How many days before expiry the warning starts.</summary>
        public int WarningDays { get; init; } = 7;

        /// <summary>How many days after expiry selling still works (with a renewal demand) before the POS stops.</summary>
        public int GraceDays { get; init; } = 7;
    }
}
