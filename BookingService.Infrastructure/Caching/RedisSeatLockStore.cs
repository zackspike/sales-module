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

    public async Task<EventSeatLockResult> TryAcquireAsync(
        Guid eventSeatId,
        string holderIdentifier,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(holderIdentifier);

        var outcome = (int)await Database.ScriptEvaluateAsync(
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

    public async Task<string?> GetHolderIdentifierAsync(Guid eventSeatId, CancellationToken cancellationToken)
    {
        var holder = await Database.StringGetAsync(RedisKeys.SeatLock(eventSeatId));
        return holder.HasValue ? holder.ToString() : null;
    }

    public async Task<IReadOnlySet<Guid>> GetLockedEventSeatIdsAsync(
        IReadOnlyCollection<Guid> eventSeatIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventSeatIds);

        if (eventSeatIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = eventSeatIds.ToArray();
        var holders = await Database.StringGetAsync(ids.Select(RedisKeys.SeatLock).ToArray());

        return ids.Where((_, i) => holders[i].HasValue).ToHashSet();
    }

    public async Task<bool> ReleaseAsync(
        Guid eventSeatId,
        string holderIdentifier,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holderIdentifier);

        var deleted = (int)await Database.ScriptEvaluateAsync(
            ReleaseScript,
            [RedisKeys.SeatLock(eventSeatId)],
            [holderIdentifier]);

        return deleted == 1;
    }
}
