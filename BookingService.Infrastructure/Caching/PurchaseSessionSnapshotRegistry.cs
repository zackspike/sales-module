using System.Runtime.CompilerServices;
using BookingService.Domain.Purchases;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Remembers the exact stored value each <see cref="PurchaseSession"/> instance was read from, so a
/// later save can detect that another writer changed it in the meantime.
/// </summary>
internal sealed class PurchaseSessionSnapshotRegistry
{
    private readonly ConditionalWeakTable<PurchaseSession, byte[]> _expectedValues = new();
    private readonly object _gate = new();

    public byte[]? ExpectedValueOf(PurchaseSession purchase)
    {
        lock (_gate)
        {
            return _expectedValues.TryGetValue(purchase, out var value) ? value : null;
        }
    }

    public void Remember(PurchaseSession purchase, byte[] value)
    {
        lock (_gate)
        {
            _expectedValues.Remove(purchase);
            _expectedValues.Add(purchase, value);
        }
    }
}
