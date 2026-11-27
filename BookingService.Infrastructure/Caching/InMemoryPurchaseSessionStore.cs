using BookingService.Application.Abstractions;
using BookingService.Domain.Purchases;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Non-distributed fallback of <see cref="RedisPurchaseSessionStore"/> for hosts without a Redis
/// connection string. It uses the same snapshot compare-and-swap, so the use cases see the same
/// behaviour; sessions simply do not survive a restart.
/// </summary>
public sealed class InMemoryPurchaseSessionStore : IPurchaseSessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, byte[]> _sessions = new();
    private readonly PurchaseSessionSnapshotRegistry _snapshots = new();

    public Task<PurchaseSession?> GetAsync(Guid purchaseId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(purchaseId, out var bytes))
            {
                return Task.FromResult<PurchaseSession?>(null);
            }

            var purchase = PurchaseSessionSnapshot.Deserialize(bytes);
            if (purchase is null || purchase.IsExpired(DateTime.UtcNow))
            {
                _sessions.Remove(purchaseId);
                return Task.FromResult<PurchaseSession?>(null);
            }

            _snapshots.Remember(purchase, bytes);
            return Task.FromResult<PurchaseSession?>(purchase);
        }
    }

    public Task SaveAsync(PurchaseSession purchase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(purchase);

        if ((purchase.ExpiresAtUtc - DateTime.UtcNow).TotalMilliseconds <= 0)
        {
            return Task.CompletedTask;
        }

        var bytes = PurchaseSessionSnapshot.Serialize(purchase);
        var expected = _snapshots.ExpectedValueOf(purchase);

        lock (_gate)
        {
            if (_sessions.TryGetValue(purchase.Id, out var current))
            {
                if (expected is null || !current.AsSpan().SequenceEqual(expected))
                {
                    throw new PurchaseSessionConcurrencyException(purchase.Id);
                }
            }
            else if (expected is not null)
            {
                throw new PurchaseSessionConcurrencyException(purchase.Id);
            }

            _sessions[purchase.Id] = bytes;
        }

        _snapshots.Remember(purchase, bytes);
        return Task.CompletedTask;
    }
}
