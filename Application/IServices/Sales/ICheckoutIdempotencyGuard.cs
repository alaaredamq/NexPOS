namespace Application.IServices.Sales
{
    /// <summary>
    /// In-process guard against duplicate checkout submissions (double-click / accidental
    /// resubmit). Must be registered as a singleton so the claim survives across requests.
    /// </summary>
    public interface ICheckoutIdempotencyGuard
    {
        /// <summary>
        /// Attempts to atomically claim <paramref name="key"/> for <paramref name="window"/>.
        /// Returns false if the key is already claimed and the claim hasn't expired yet -
        /// callers should treat that as "this checkout was already submitted".
        /// </summary>
        bool TryBeginCheckout(string key, TimeSpan window);

        /// <summary>
        /// Releases a claim early. Call this when a checkout attempt fails, so a genuine
        /// retry of the same cart/items isn't blocked for the rest of the window.
        /// </summary>
        void ReleaseCheckout(string key);
    }
}
