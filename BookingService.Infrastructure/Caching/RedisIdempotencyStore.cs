using System.Text.Json;
using BookingService.Application.Abstractions;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Used idempotency keys stored as Redis strings <c>booking:idempotency:{key} = {"EventId","EventSeatId"}</c> with a TTL.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyStore(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
    }

    private IDatabase Database => _redis.GetDatabase();

    public IdempotencyRecord? GetIdempotencyRecord(Guid idempotencyKey)
    {
        var value = Database.StringGet(RedisKeys.Idempotency(idempotencyKey));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<IdempotencyRecord>((byte[])value!);
        }
        catch (JsonException)
        {
            return null;
        }
    }


    public IdempotencyRecord? Get(Guid idempotencyKey) => GetIdempotencyRecord(idempotencyKey);

    public void Remember(Guid idempotencyKey, IdempotencyRecord record, TimeSpan retention)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        Database.StringSet(
            RedisKeys.Idempotency(idempotencyKey),
            bytes,
            retention);
    }
}
