using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Null Object pattern implementation used when Redis is not configured: every lookup misses,
/// so handlers fall back to the durable repository as the source of truth for availability and idempotency.
/// </summary>
public sealed class NoCache : IIdempotencyStore, IAvailableEventSeatsCache
{
    public Task<IdempotencyRecord?> GetIdempotencyRecordAsync(
        Guid idempotencyKey,
        CancellationToken cancellationToken) => Task.FromResult<IdempotencyRecord?>(null);

    public Task RememberAsync(
        Guid idempotencyKey,
        IdempotencyRecord record,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvailableEventSeat>?> GetAsync(
        Guid eventId,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AvailableEventSeat>?>(null);

    public Task SetAsync(
        Guid eventId,
        IReadOnlyList<AvailableEventSeat> seats,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
