using System.Text.Json;
using BookingService.Application.Abstractions;
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

    public IdempotencyRecord? Get(Guid idempotencyKey)
    {
        var value = _redis.GetDatabase().StringGet(RedisKeys.Idempotency(idempotencyKey));
        return value.HasValue ? JsonSerializer.Deserialize<IdempotencyRecord>(value.ToString()) : null;
    }

    public void Remember(Guid idempotencyKey, IdempotencyRecord record, TimeSpan retention)
    {
        _redis.GetDatabase().StringSet(
            RedisKeys.Idempotency(idempotencyKey),
            JsonSerializer.Serialize(record),
            retention);
    }
}
