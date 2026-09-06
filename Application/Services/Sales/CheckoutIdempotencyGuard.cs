using System.Collections.Concurrent;
using Application.IServices.Sales;

namespace Application.Services.Sales
{
    public class CheckoutIdempotencyGuard : ICheckoutIdempotencyGuard
    {
        private readonly ConcurrentDictionary<string, DateTime> _claimedUntil = new();

        public bool TryBeginCheckout(string key, TimeSpan window)
        {
            var now = DateTime.UtcNow;
            var newExpiry = now.Add(window);
            var claimed = false;

            _claimedUntil.AddOrUpdate(
                key,
                _ => { claimed = true; return newExpiry; },
                (_, existingExpiry) =>
                {
                    if (existingExpiry <= now)
                    {
                        // Previous claim expired (e.g. the earlier attempt never released it
                        // due to a crash) - treat this as a fresh attempt.
                        claimed = true;
                        return newExpiry;
                    }

                    // Still within a live claim's window: reject as a duplicate.
                    return existingExpiry;
                });

            return claimed;
        }

        public void ReleaseCheckout(string key)
        {
            _claimedUntil.TryRemove(key, out _);
        }
    }
}
