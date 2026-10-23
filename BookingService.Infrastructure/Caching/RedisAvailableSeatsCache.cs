using System.Text.Json;
using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Available seats of an event cached in Redis as JSON under <c>booking:event:{eventId}:available-seats</c>.
/// The short TTL bounds how long a stale copy (e.g. rebuilt while a sale was committing) can live;
/// reservations and purchases always re-check the repository.
/// </summary>
public sealed class RedisAvailableSeatsCache : IAvailableEventSeatsCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private readonly IConnectionMultiplexer _redis;

    public RedisAvailableSeatsCache(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
    }

    private IDatabase Database => _redis.GetDatabase();

    public IReadOnlyList<AvailableEventSeat>? Get(Guid eventId)
    {
        var value = Database.StringGet(RedisKeys.AvailableSeats(eventId));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<AvailableEventSeat>>((byte[])value!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Set(Guid eventId, IReadOnlyList<AvailableEventSeat> seats)
    {
        ArgumentNullException.ThrowIfNull(seats);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(seats);
        Database.StringSet(RedisKeys.AvailableSeats(eventId), bytes, TimeToLive);
    }

    public void Invalidate(Guid eventId)
    {
        Database.KeyDelete(RedisKeys.AvailableSeats(eventId));
    }
}
