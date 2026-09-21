namespace BookingService.Application.Tickets;

/// <summary>
/// Lookup used by the purchase validation to check that an event is known.
/// Implemented by Infrastructure on top of the in-memory event store.
/// </summary>
public interface IEventCatalog
{
    bool Exists(Guid eventId);
}
