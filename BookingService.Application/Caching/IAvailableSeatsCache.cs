using BookingService.Application.Tickets.Dtos;

namespace BookingService.Application.Caching;

/// <summary>
/// Short-lived copy of the seats an event has available in the database, so the high-frequency
/// "available seats" query is served from memory. Seat locks are applied on top at read time.
/// </summary>
public interface IAvailableSeatsCache
{
    /// <summary>
    /// Returns the cached available seats of <paramref name="eventId"/>, or null on a cache miss.
    /// </summary>
    Task<IReadOnlyList<SeatAvailabilityDto>?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task SetAsync(
        Guid eventId,
        IReadOnlyList<SeatAvailabilityDto> seats,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the cached seats of <paramref name="eventId"/> after one of them is sold.
    /// </summary>
    Task InvalidateAsync(Guid eventId, CancellationToken cancellationToken = default);
}
