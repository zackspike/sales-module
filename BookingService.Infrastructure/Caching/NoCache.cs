using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Null Object pattern implementation used when Redis is not configured: every lookup misses,
/// so handlers fall back to the durable repository as the source of truth for availability and idempotency.
/// </summary>
public sealed class NoCache : IIdempotencyStore, IAvailableEventSeatsCache
{
    public IdempotencyRecord? GetIdempotencyRecord(Guid idempotencyKey) => null;

    public void Remember(Guid idempotencyKey, IdempotencyRecord record, TimeSpan retention)
    {
    }

    public IReadOnlyList<AvailableEventSeat>? Get(Guid eventId) => null;

    public void Set(Guid eventId, IReadOnlyList<AvailableEventSeat> seats)
    {
    }

    public void Invalidate(Guid eventId)
    {
    }
}
