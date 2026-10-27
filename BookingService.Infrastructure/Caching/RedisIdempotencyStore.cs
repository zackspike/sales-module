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

    public async Task<IdempotencyRecord?> GetIdempotencyRecordAsync(
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var value = await Database.StringGetAsync(RedisKeys.Idempotency(idempotencyKey));
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

    public async Task RememberAsync(
        Guid idempotencyKey,
        IdempotencyRecord record,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        await Database.StringSetAsync(
            RedisKeys.Idempotency(idempotencyKey),
            bytes,
            retention);
    }
}
