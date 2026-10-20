using BookingService.Application.Abstractions;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Event seat locks stored as Redis strings <c>booking:seat-lock:{eventSeatId} = {holderIdentifier}</c> with a TTL.
/// Acquire and release run as Lua scripts, so the check and the write are one atomic step.
/// </summary>
public sealed class RedisSeatLockStore : IEventSeatLockStore
{
    // Returns 1 when the lock was taken, 2 when the caller already held it (TTL renewed), 0 otherwise.
    private const string AcquireScript = """
        local holder = redis.call('GET', KEYS[1])
        if not holder then
            redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
            return 1
        end
        if holder == ARGV[1] then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            return 2
        end
        return 0
        """;

    // Deletes the lock only if it still belongs to the caller.
    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private readonly IConnectionMultiplexer _redis;

    public RedisSeatLockStore(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
    }

    private IDatabase Database => _redis.GetDatabase();

    public EventSeatLockResult TryAcquire(Guid eventSeatId, string holderIdentifier, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(holderIdentifier);

        var outcome = (int)Database.ScriptEvaluate(
            AcquireScript,
            [RedisKeys.SeatLock(eventSeatId)],
            [holderIdentifier, (long)duration.TotalMilliseconds]);

        return outcome switch
        {
            1 => EventSeatLockResult.Acquired,
            2 => EventSeatLockResult.Renewed,
            _ => EventSeatLockResult.HeldByAnother
        };
    }

    public string? GetHolderIdentifier(Guid eventSeatId)
    {
        var holder = Database.StringGet(RedisKeys.SeatLock(eventSeatId));
        return holder.HasValue ? holder.ToString() : null;
    }

    public string? GetHolder(Guid eventSeatId) => GetHolderIdentifier(eventSeatId);

    public IReadOnlySet<Guid> GetLockedEventSeatIds(IReadOnlyCollection<Guid> eventSeatIds)
    {
        ArgumentNullException.ThrowIfNull(eventSeatIds);

        if (eventSeatIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = eventSeatIds.ToArray();
        var holders = Database.StringGet(ids.Select(RedisKeys.SeatLock).ToArray());

        return ids.Where((_, i) => holders[i].HasValue).ToHashSet();
    }


    public IReadOnlySet<Guid> GetLockedTicketIds(IReadOnlyCollection<Guid> eventSeatIds) => GetLockedEventSeatIds(eventSeatIds);

    public bool Release(Guid eventSeatId, string holderIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holderIdentifier);

        var deleted = (int)Database.ScriptEvaluate(
            ReleaseScript,
            [RedisKeys.SeatLock(eventSeatId)],
            [holderIdentifier]);

        return deleted == 1;
    }
}
