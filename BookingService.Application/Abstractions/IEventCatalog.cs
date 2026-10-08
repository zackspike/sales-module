namespace BookingService.Application.Abstractions;

/// <summary>
/// Lookup used by purchase and availability queries to check that an event is known.
/// Implemented by Infrastructure (in-memory or PostgreSQL event store).
/// </summary>
public interface IEventCatalog
{
    bool Exists(Guid eventId);
}
