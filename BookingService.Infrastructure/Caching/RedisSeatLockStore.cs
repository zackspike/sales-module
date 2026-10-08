using BookingService.Application.Abstractions;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Seat locks as Redis strings <c>booking:seat-lock:{ticketId} = {holder}</c> with a TTL.
/// Acquire and release run as Lua scripts, so the check and the write are one atomic step.
/// </summary>
public sealed class RedisSeatLockStore : ISeatLockStore
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
        _redis = redis;
    }

    public SeatLockResult TryAcquire(Guid ticketId, string holder, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        var outcome = (int)_redis.GetDatabase().ScriptEvaluate(
            AcquireScript,
            [RedisKeys.SeatLock(ticketId)],
            [holder, (long)duration.TotalMilliseconds]);

        return outcome switch
        {
            1 => SeatLockResult.Acquired,
            2 => SeatLockResult.Renewed,
            _ => SeatLockResult.HeldByAnother
        };
    }

    public string? GetHolder(Guid ticketId)
    {
        var holder = _redis.GetDatabase().StringGet(RedisKeys.SeatLock(ticketId));
        return holder.HasValue ? holder.ToString() : null;
    }

    public IReadOnlySet<Guid> GetLockedTicketIds(IReadOnlyCollection<Guid> ticketIds)
    {
        if (ticketIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = ticketIds.ToArray();
        var holders = _redis.GetDatabase().StringGet(ids.Select(RedisKeys.SeatLock).ToArray());

        return ids.Where((_, i) => holders[i].HasValue).ToHashSet();
    }

    public bool Release(Guid ticketId, string holder)
    {
        var deleted = (int)_redis.GetDatabase().ScriptEvaluate(
            ReleaseScript,
            [RedisKeys.SeatLock(ticketId)],
            [holder]);

        return deleted == 1;
    }
}
