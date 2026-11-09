using Common.Licensing;

namespace Application.DTOs.Licensing
{
    public enum LicenseStatus
    {
        /// <summary>Valid, nothing to warn about.</summary>
        Active,

        /// <summary>Time-limited license in its last week. Selling continues; a warning is shown.</summary>
        ExpiringSoon,

        /// <summary>Time-limited license expired within the last week. Selling continues; renewal is demanded.</summary>
        GracePeriod,

        /// <summary>Time-limited license expired more than a week ago. POS and new sales are stopped; data stays.</summary>
        Expired,

        /// <summary>No license on this computer. POS and new sales are stopped.</summary>
        NotActivated,

        /// <summary>A license file exists but its signature fails. POS and new sales are stopped.</summary>
        Invalid,

        /// <summary>The license was issued for a different computer. POS and new sales are stopped.</summary>
        WrongMachine,

        /// <summary>A perpetual license whose updates plan ended before this build was released. POS and new sales are stopped.</summary>
        UpdatesNotCovered
    }

    public sealed record LicenseState
    {
        public required LicenseStatus Status { get; init; }

        /// <summary>This computer's id in the form XXXXX-XXXXX-XXXXX-XXXXX, shown so the customer can send it to the issuer.</summary>
        public required string MachineId { get; init; }

        public required DateOnly BuildDate { get; init; }

        /// <summary>True in a Debug build, where nothing is enforced.</summary>
        public bool EnforcementDisabled { get; init; }

        public string? Customer { get; init; }
        public LicenseKind? Kind { get; init; }
        public string? Serial { get; init; }
        public DateOnly? IssuedOn { get; init; }

        /// <summary>How many PCs the installed key covers (the store server plus its tills). 1 while nothing is installed.</summary>
        public int Seats { get; init; } = 1;

        /// <summary>Full licenses: last day of the updates plan. Provisional/Trial: last valid day.</summary>
        public DateOnly? ValidUntil { get; init; }

        /// <summary>ExpiringSoon only: days until the license expires (0 = expires today).</summary>
        public int? DaysLeft { get; init; }

        /// <summary>Time-limited licenses: last day on which selling still works (expiry + grace).</summary>
        public DateOnly? SalesStopAfter { get; init; }

        /// <summary>GracePeriod only: days until selling stops (0 = today is the last day).</summary>
        public int? DaysUntilStop { get; init; }

        public bool AllowsSales => Status is LicenseStatus.Active or LicenseStatus.ExpiringSoon or LicenseStatus.GracePeriod;

        public bool ShowsWarning => Status is LicenseStatus.ExpiringSoon or LicenseStatus.GracePeriod;
    }

    public enum LicenseActivationError
    {
        None,
        Malformed,
        BadSignature,
        UnsupportedVersion,
        WrongMachine,
        OlderThanCurrent,
        StorageFailed
    }

    public sealed record LicenseActivationResult(bool Success, LicenseActivationError Error, LicenseState State);
}
