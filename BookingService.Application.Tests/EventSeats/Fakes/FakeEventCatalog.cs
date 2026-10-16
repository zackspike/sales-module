using BookingService.Application.Abstractions;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeEventCatalog(params Guid[] eventIds) : IEventCatalog
{
    private readonly HashSet<Guid> _eventIds = eventIds.ToHashSet();

    public List<Guid> RequestedIds { get; } = [];

    public bool Exists(Guid eventId)
    {
        RequestedIds.Add(eventId);
        return _eventIds.Contains(eventId);
    }
}
