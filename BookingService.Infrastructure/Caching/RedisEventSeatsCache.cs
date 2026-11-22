using System.Text.Json;
using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Event seats of an event and their stored status, cached in Redis as JSON under <c>booking:event:{eventId}:seats</c>.
/// The short TTL bounds how long a stale copy (e.g. rebuilt while a sale was committing) can live;
/// reservations and purchases always re-check the repository.
/// </summary>
public sealed class RedisEventSeatsCache : IEventSeatsCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private readonly IConnectionMultiplexer _redis;

    public RedisEventSeatsCache(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
    }

    private IDatabase Database => _redis.GetDatabase();

    public async Task<IReadOnlyList<EventSeatAvailability>?> GetAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var value = await Database.StringGetAsync(RedisKeys.EventSeats(eventId));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<EventSeatAvailability>>((byte[])value!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SetAsync(
        Guid eventId,
        IReadOnlyList<EventSeatAvailability> seats,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seats);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(seats);
        await Database.StringSetAsync(RedisKeys.EventSeats(eventId), bytes, TimeToLive);
    }

    public async Task InvalidateAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await Database.KeyDeleteAsync(RedisKeys.EventSeats(eventId));
    }
}
