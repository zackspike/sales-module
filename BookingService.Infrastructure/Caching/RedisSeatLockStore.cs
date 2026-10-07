using BookingService.Application.Caching;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Seat locks as Redis strings <c>booking:seat-lock:{ticketId} = {userId}</c> with a TTL.
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

    public async Task<SeatLockResult> TryAcquireAsync(
        Guid ticketId,
        Guid userId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "The lock duration must be positive.");
        }

        var outcome = (int)await _redis.GetDatabase().ScriptEvaluateAsync(
            AcquireScript,
            [RedisKeys.SeatLock(ticketId)],
            [userId.ToString(), (long)duration.TotalMilliseconds]);

        return outcome switch
        {
            1 => SeatLockResult.Acquired,
            2 => SeatLockResult.Renewed,
            _ => SeatLockResult.HeldByAnotherUser
        };
    }

    public async Task<Guid?> GetHolderAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var holder = await _redis.GetDatabase().StringGetAsync(RedisKeys.SeatLock(ticketId));
        return holder.HasValue && Guid.TryParse(holder.ToString(), out var userId) ? userId : null;
    }

    public async Task<IReadOnlySet<Guid>> GetLockedTicketIdsAsync(
        IReadOnlyCollection<Guid> ticketIds,
        CancellationToken cancellationToken = default)
    {
        if (ticketIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = ticketIds.ToArray();
        var holders = await _redis.GetDatabase().StringGetAsync(ids.Select(RedisKeys.SeatLock).ToArray());

        var locked = new HashSet<Guid>();
        for (var i = 0; i < ids.Length; i++)
        {
            if (holders[i].HasValue)
            {
                locked.Add(ids[i]);
            }
        }

        return locked;
    }

    public async Task<bool> ReleaseAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken = default)
    {
        var deleted = (int)await _redis.GetDatabase().ScriptEvaluateAsync(
            ReleaseScript,
            [RedisKeys.SeatLock(ticketId)],
            [userId.ToString()]);

        return deleted == 1;
    }
}
