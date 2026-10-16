using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeAvailableEventSeatsCache : IAvailableEventSeatsCache
{
    public Dictionary<Guid, IReadOnlyList<AvailableEventSeat>> Values { get; } = [];

    public int SetCalls { get; private set; }

    public int InvalidateCalls { get; private set; }

    public IReadOnlyList<AvailableEventSeat>? Get(Guid eventId) => Values.GetValueOrDefault(eventId);

    public void Set(Guid eventId, IReadOnlyList<AvailableEventSeat> seats)
    {
        SetCalls++;
        Values[eventId] = seats;
    }

    public void Invalidate(Guid eventId)
    {
        InvalidateCalls++;
        Values.Remove(eventId);
    }
}
