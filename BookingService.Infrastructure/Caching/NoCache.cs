using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Dtos;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Used when Redis is not configured: every lookup misses, so handlers fall back to the ticket
/// repository, which already is the durable source of truth for availability and idempotency.
/// </summary>
public sealed class NoCache : IIdempotencyStore, IAvailableSeatsCache
{
    public IdempotencyRecord? Get(Guid idempotencyKey) => null;

    public void Remember(Guid idempotencyKey, IdempotencyRecord record, TimeSpan retention)
    {
    }

    IReadOnlyList<SeatAvailabilityDto>? IAvailableSeatsCache.Get(Guid eventId) => null;

    public void Set(Guid eventId, IReadOnlyList<SeatAvailabilityDto> seats)
    {
    }

    public void Invalidate(Guid eventId)
    {
    }
}
