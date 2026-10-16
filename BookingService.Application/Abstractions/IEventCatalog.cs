namespace BookingService.Application.Abstractions;

/// <summary>
/// Data from EventService. Read-only.
/// Used to reject requests for unknown events (404).
/// </summary>
public interface IEventCatalog
{
    /// <summary>
    /// Returns true if the event with the given id exists, false otherwise.
    /// </summary>
    bool Exists(Guid eventId);
}
