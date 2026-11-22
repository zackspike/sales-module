using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Short-lived copy of the event seats of an event with their stored status (available or sold), so the
/// high-frequency "event seats" query is served from memory. Seat locks are applied on top at read time.
/// </summary>
public interface IEventSeatsCache
{
    /// <summary>
    /// Returns the cached seats of <paramref name="eventId"/>, or null on a cache miss.
    /// </summary>
    Task<IReadOnlyList<EventSeatAvailability>?> GetAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the seats of <paramref name="eventId"/> for a short time. Only call it for existing
    /// events: a cache hit is taken as proof that the event exists.
    /// </summary>
    Task SetAsync(Guid eventId, IReadOnlyList<EventSeatAvailability> seats, CancellationToken cancellationToken);

    /// <summary>
    /// Drops the cached seats of <paramref name="eventId"/> after one of them is sold.
    /// </summary>
    Task InvalidateAsync(Guid eventId, CancellationToken cancellationToken);
}
