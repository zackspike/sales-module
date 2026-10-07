using System.Text.Json;
using BookingService.Application.Caching;
using BookingService.Application.Tickets.Dtos;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Available seats of an event as a JSON Redis string <c>booking:event:{eventId}:available-seats</c>.
/// The short TTL bounds how long a stale copy (e.g. rebuilt while a sale was committing) can live;
/// reservations and purchases always re-check the database.
/// </summary>
public sealed class RedisAvailableSeatsCache : IAvailableSeatsCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private readonly IConnectionMultiplexer _redis;

    public RedisAvailableSeatsCache(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<IReadOnlyList<SeatAvailabilityDto>?> GetAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(RedisKeys.AvailableSeats(eventId));
        return value.HasValue ? JsonSerializer.Deserialize<List<SeatAvailabilityDto>>(value.ToString()) : null;
    }

    public Task SetAsync(
        Guid eventId,
        IReadOnlyList<SeatAvailabilityDto> seats,
        CancellationToken cancellationToken = default)
    {
        return _redis.GetDatabase().StringSetAsync(
            RedisKeys.AvailableSeats(eventId),
            JsonSerializer.Serialize(seats),
            TimeToLive);
    }

    public Task InvalidateAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return _redis.GetDatabase().KeyDeleteAsync(RedisKeys.AvailableSeats(eventId));
    }
}
