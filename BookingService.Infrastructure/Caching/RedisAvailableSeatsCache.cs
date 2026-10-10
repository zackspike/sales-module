using System.Text.Json;
using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Dtos;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Available seats of an event as a JSON Redis string <c>booking:event:{eventId}:available-seats</c>.
/// The short TTL bounds how long a stale copy (e.g. rebuilt while a sale was committing) can live;
/// reservations and purchases always re-check the repository.
/// </summary>
public sealed class RedisAvailableSeatsCache : IAvailableSeatsCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private readonly IConnectionMultiplexer _redis;

    public RedisAvailableSeatsCache(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public IReadOnlyList<SeatAvailabilityDto>? Get(Guid eventId)
    {
        var value = _redis.GetDatabase().StringGet(RedisKeys.AvailableSeats(eventId));
        return value.HasValue ? JsonSerializer.Deserialize<List<SeatAvailabilityDto>>(value.ToString()) : null;
    }

    public void Set(Guid eventId, IReadOnlyList<SeatAvailabilityDto> seats)
    {
        _redis.GetDatabase().StringSet(RedisKeys.AvailableSeats(eventId), JsonSerializer.Serialize(seats), TimeToLive);
    }

    public void Invalidate(Guid eventId)
    {
        _redis.GetDatabase().KeyDelete(RedisKeys.AvailableSeats(eventId));
    }
}
