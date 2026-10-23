using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Short-lived copy of the event seats an event has available in the database, so the high-frequency
/// "available seats" query is served from memory. Seat locks are applied on top at read time.
/// </summary>
public interface IAvailableEventSeatsCache
{
    /// <summary>
    /// Returns the cached available seats of <paramref name="eventId"/>, or null on a cache miss.
    /// </summary>
    Task<IReadOnlyList<AvailableEventSeat>?> GetAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the available seats of <paramref name="eventId"/> for a short time. Only call it for existing
    /// events: a cache hit is taken as proof that the event exists.
    /// </summary>
    Task SetAsync(Guid eventId, IReadOnlyList<AvailableEventSeat> seats, CancellationToken cancellationToken);

    /// <summary>
    /// Drops the cached seats of <paramref name="eventId"/> after one of them is sold.
    /// </summary>
    Task InvalidateAsync(Guid eventId, CancellationToken cancellationToken);
}
