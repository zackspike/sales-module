using BookingService.Domain.Events;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Data from EventService. Read-only.
/// Used to reject requests for unknown events (404) and to read the event data a use case needs.
/// </summary>
public interface IEventCatalog
{
    /// <summary>
    /// Returns true if the event with the given id exists, false otherwise.
    /// </summary>
    bool Exists(Guid eventId);

    /// <summary>
    /// Returns the event with the given id, or null if it does not exist.
    /// </summary>
    Event? GetById(Guid eventId);
}
