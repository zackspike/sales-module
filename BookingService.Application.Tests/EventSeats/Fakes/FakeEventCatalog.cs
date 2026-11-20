using BookingService.Application.Abstractions;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeEventCatalog(params Guid[] eventIds) : IEventCatalog
{
    private readonly HashSet<Guid> _eventIds = eventIds.ToHashSet();

    public List<Guid> RequestedIds { get; } = [];

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        RequestedIds.Add(eventId);
        return Task.FromResult(_eventIds.Contains(eventId));
    }
}
