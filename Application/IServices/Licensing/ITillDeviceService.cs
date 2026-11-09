using Application.DTOs.Licensing;

namespace Application.IServices.Licensing
{
    /// <summary>
    /// The PCs allowed to sell through this store server, and the licence's seat count that limits them. The server PC itself is
    /// the first seat and is never registered; a till is a browser on another computer that pairs once with a one-time code.
    /// Seats are decided here, offline, from the signed key: never by the cloud.
    /// </summary>
    public interface ITillDeviceService
    {
        Task<TillSeatsInfo> GetSeatsAsync(CancellationToken ct = default);

        /// <summary>Reserves a seat for a new till and returns its pairing code (valid <see cref="TillDeviceService.CodeLifetime"/>).</summary>
        Task<IssuePairingResult> IssuePairingCodeAsync(string name, CancellationToken ct = default);

        /// <summary>
        /// Called by the till itself (no login). Exchanges a valid code for the device token. <paramref name="clientKey"/> (the caller's
        /// address) throttles guessing: too many wrong codes in a row are refused for a while.
        /// </summary>
        Task<PairResult> PairAsync(string? code, string clientKey, CancellationToken ct = default);

        /// <summary>Removes a till (or cancels an unused code) and frees its seat. False if there is no such till.</summary>
        Task<bool> RevokeAsync(int id, CancellationToken ct = default);

        /// <summary>Whether the till presenting <paramref name="token"/> may sell right now.</summary>
        Task<TillAccess> CheckAccessAsync(string? token, CancellationToken ct = default);
    }
}
