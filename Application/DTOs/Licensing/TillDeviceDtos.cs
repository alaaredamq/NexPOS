namespace Application.DTOs.Licensing
{
    public enum TillDeviceState
    {
        /// <summary>Added by an administrator, waiting for the till to enter its pairing code. Holds a seat until the code expires.</summary>
        Pending,

        /// <summary>Paired and allowed to sell.</summary>
        Active
    }

    /// <summary>One till in the list on the License page.</summary>
    /// <param name="Overflow">Active, but the licence now covers fewer PCs than are registered (a smaller package was installed): this till is refused until a seat is freed.</param>
    public sealed record TillDeviceInfo(
        int Id,
        string Name,
        TillDeviceState State,
        DateTime CreatedAt,
        DateTime? PairedAt,
        DateTime? LastSeenAt,
        DateTime? PairingExpiresAt,
        bool Overflow);

    /// <param name="Seats">PCs the licence covers, the store server included.</param>
    /// <param name="TillSeats">Seats left for tills: <paramref name="Seats"/> minus the server. Unlimited (int.MaxValue) in a build that does not enforce licences.</param>
    /// <param name="Used">Tills that hold a seat: active ones plus pairing codes still open.</param>
    public sealed record TillSeatsInfo(int Seats, int TillSeats, int Used, bool Enforced, IReadOnlyList<TillDeviceInfo> Devices);

    public enum IssuePairingOutcome { Success, SeatsFull, LicenseInactive, NameRequired }

    /// <param name="Code">Shown once, in the form XXXX-XXXX. Only its hash is stored.</param>
    public sealed record IssuePairingResult(IssuePairingOutcome Outcome, int? DeviceId = null, string? Code = null, DateTime? ExpiresAt = null);

    public enum PairOutcome { Success, InvalidCode, SeatsFull, LicenseInactive, TooManyAttempts }

    /// <param name="Token">Shown once; the till keeps it in a cookie. Only its hash is stored.</param>
    public sealed record PairResult(PairOutcome Outcome, string? Token = null, string? Name = null);

    public enum TillAccess
    {
        Allowed,

        /// <summary>No token, an unknown token, or a token whose till was removed.</summary>
        NotRegistered,

        /// <summary>A known till, but the licence now covers fewer PCs than are registered and this one is over the limit.</summary>
        SeatsExceeded
    }
}
