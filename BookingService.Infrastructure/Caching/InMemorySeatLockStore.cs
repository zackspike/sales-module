using BookingService.Application.Abstractions;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Single-process event seat locks, used when Redis is not configured. Same contract as
/// <see cref="RedisSeatLockStore"/>, but locks are lost on restart and not shared between instances.
/// </summary>
public sealed class InMemorySeatLockStore : IEventSeatLockStore
{
    private readonly Dictionary<Guid, LockEntry> _locks = new();
    private readonly Lock _lock = new();
    private readonly TimeProvider _clock;

    public InMemorySeatLockStore(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public EventSeatLockResult TryAcquire(Guid eventSeatId, string holderIdentifier, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(holderIdentifier);

        lock (_lock)
        {
            var current = CurrentHolder(eventSeatId);
            if (current is not null && current != holderIdentifier)
            {
                return EventSeatLockResult.HeldByAnother;
            }

            _locks[eventSeatId] = new LockEntry(holderIdentifier, _clock.GetUtcNow() + duration);
            return current is null ? EventSeatLockResult.Acquired : EventSeatLockResult.Renewed;
        }
    }

    public string? GetHolderIdentifier(Guid eventSeatId)
    {
        lock (_lock)
        {
            return CurrentHolder(eventSeatId);
        }
    }


    public string? GetHolder(Guid eventSeatId) => GetHolderIdentifier(eventSeatId);

    public IReadOnlySet<Guid> GetLockedEventSeatIds(IReadOnlyCollection<Guid> eventSeatIds)
    {
        ArgumentNullException.ThrowIfNull(eventSeatIds);

        if (eventSeatIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        lock (_lock)
        {
            return eventSeatIds.Where(id => CurrentHolder(id) is not null).ToHashSet();
        }
    }


    public IReadOnlySet<Guid> GetLockedTicketIds(IReadOnlyCollection<Guid> eventSeatIds) => GetLockedEventSeatIds(eventSeatIds);

    public bool Release(Guid eventSeatId, string holderIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holderIdentifier);

        lock (_lock)
        {
            return CurrentHolder(eventSeatId) == holderIdentifier && _locks.Remove(eventSeatId);
        }
    }

    // Caller holds _lock. Expired entries are dropped lazily.
    private string? CurrentHolder(Guid eventSeatId)
    {
        if (!_locks.TryGetValue(eventSeatId, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt > _clock.GetUtcNow())
        {
            return entry.HolderIdentifier;
        }

        _locks.Remove(eventSeatId);
        return null;
    }

    private readonly record struct LockEntry(string HolderIdentifier, DateTimeOffset ExpiresAt);
}
