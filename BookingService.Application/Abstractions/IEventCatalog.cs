using BookingService.Domain.Events;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Lookup used by purchase and availability queries to check that an event is known.
/// Implemented by Infrastructure on top of the in-memory event store.
/// </summary>
public interface IEventCatalog
{
    bool Exists(Guid eventId);
    Event? GetById(Guid eventId) => null;
    IReadOnlyCollection<Event> GetAll() => [];
}
