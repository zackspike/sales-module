using BookingService.Application.Tickets.Dtos;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Short-lived copy of the seats an event has available in the database, so the high-frequency
/// "available seats" query is served from memory. Seat locks are applied on top at read time.
/// </summary>
public interface IAvailableSeatsCache
{
    /// <summary>
    /// Returns the cached available seats of <paramref name="eventId"/>, or null on a cache miss.
    /// </summary>
    IReadOnlyList<SeatAvailabilityDto>? Get(Guid eventId);

    void Set(Guid eventId, IReadOnlyList<SeatAvailabilityDto> seats);

    /// <summary>
    /// Drops the cached seats of <paramref name="eventId"/> after one of them is sold.
    /// </summary>
    void Invalidate(Guid eventId);
}
