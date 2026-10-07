using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Key layout of the booking data kept in Redis.
/// </summary>
internal static class RedisKeys
{
    public static RedisKey SeatLock(Guid ticketId) => $"booking:seat-lock:{ticketId}";

    public static RedisKey Idempotency(Guid idempotencyKey) => $"booking:idempotency:{idempotencyKey}";

    public static RedisKey AvailableSeats(Guid eventId) => $"booking:event:{eventId}:available-seats";
}
