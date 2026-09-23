using BookingService.Application.Tickets;

namespace BookingService.Infrastructure;

public class InMemoryEventCatalog : IEventCatalog
{
    private readonly HashSet<Guid> _knownEvents =
    [
        Guid.Parse("11111111-1111-1111-1111-111111111111")
    ];

    public bool Exists(Guid eventId) => _knownEvents.Contains(eventId);
}
