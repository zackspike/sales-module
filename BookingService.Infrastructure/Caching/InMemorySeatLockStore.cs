using BookingService.Application.Abstractions;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Single-process seat locks, used when Redis is not configured. Same contract as
/// <see cref="RedisSeatLockStore"/>, but locks are lost on restart and not shared between instances.
/// </summary>
public sealed class InMemorySeatLockStore : ISeatLockStore
{
    private readonly Dictionary<Guid, (string Holder, DateTimeOffset ExpiresAt)> _locks = new();
    private readonly Lock _lock = new();
    private readonly TimeProvider _clock;

    public InMemorySeatLockStore(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public SeatLockResult TryAcquire(Guid ticketId, string holder, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        lock (_lock)
        {
            var current = CurrentHolder(ticketId);
            if (current is not null && current != holder)
            {
                return SeatLockResult.HeldByAnother;
            }

            _locks[ticketId] = (holder, _clock.GetUtcNow() + duration);
            return current is null ? SeatLockResult.Acquired : SeatLockResult.Renewed;
        }
    }

    public string? GetHolder(Guid ticketId)
    {
        lock (_lock)
        {
            return CurrentHolder(ticketId);
        }
    }

    public IReadOnlySet<Guid> GetLockedTicketIds(IReadOnlyCollection<Guid> ticketIds)
    {
        lock (_lock)
        {
            return ticketIds.Where(id => CurrentHolder(id) is not null).ToHashSet();
        }
    }

    public bool Release(Guid ticketId, string holder)
    {
        lock (_lock)
        {
            return CurrentHolder(ticketId) == holder && _locks.Remove(ticketId);
        }
    }

    // Caller holds _lock. Expired entries are dropped lazily.
    private string? CurrentHolder(Guid ticketId)
    {
        if (!_locks.TryGetValue(ticketId, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt > _clock.GetUtcNow())
        {
            return entry.Holder;
        }

        _locks.Remove(ticketId);
        return null;
    }
}
