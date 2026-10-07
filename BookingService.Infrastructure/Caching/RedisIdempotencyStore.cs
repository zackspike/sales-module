using System.Text.Json;
using BookingService.Application.Caching;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Used idempotency keys as Redis strings <c>booking:idempotency:{key} = {"EventId","TicketId"}</c> with a TTL.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<IdempotencyRecord?> GetAsync(Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(RedisKeys.Idempotency(idempotencyKey));
        return value.HasValue ? JsonSerializer.Deserialize<IdempotencyRecord>(value.ToString()) : null;
    }

    public Task RememberAsync(
        Guid idempotencyKey,
        IdempotencyRecord record,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        return _redis.GetDatabase().StringSetAsync(
            RedisKeys.Idempotency(idempotencyKey),
            JsonSerializer.Serialize(record),
            retention);
    }
}
