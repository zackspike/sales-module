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

    public Task<EventSeatLockResult> TryAcquireAsync(
        Guid eventSeatId,
        string holderIdentifier,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(holderIdentifier);

        lock (_lock)
        {
            var current = CurrentHolder(eventSeatId);
            if (current is not null && current != holderIdentifier)
            {
                return Task.FromResult(EventSeatLockResult.HeldByAnother);
            }

            _locks[eventSeatId] = new LockEntry(holderIdentifier, _clock.GetUtcNow() + duration);
            return Task.FromResult(current is null ? EventSeatLockResult.Acquired : EventSeatLockResult.Renewed);
        }
    }

    public Task<string?> GetHolderIdentifierAsync(Guid eventSeatId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(CurrentHolder(eventSeatId));
        }
    }

    public Task<IReadOnlySet<Guid>> GetLockedEventSeatIdsAsync(
        IReadOnlyCollection<Guid> eventSeatIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventSeatIds);

        if (eventSeatIds.Count == 0)
        {
            return Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
        }

        lock (_lock)
        {
            return Task.FromResult<IReadOnlySet<Guid>>(
                eventSeatIds.Where(id => CurrentHolder(id) is not null).ToHashSet());
        }
    }

    public Task<bool> ReleaseAsync(
        Guid eventSeatId,
        string holderIdentifier,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holderIdentifier);

        lock (_lock)
        {
            return Task.FromResult(CurrentHolder(eventSeatId) == holderIdentifier && _locks.Remove(eventSeatId));
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
