using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeEventSeatsCache : IEventSeatsCache
{
    public Dictionary<Guid, IReadOnlyList<EventSeatAvailability>> Values { get; } = [];

    public int SetCalls { get; private set; }

    public int InvalidateCalls { get; private set; }

    public Task<IReadOnlyList<EventSeatAvailability>?> GetAsync(Guid eventId, CancellationToken cancellationToken) =>
        Task.FromResult(Values.GetValueOrDefault(eventId));

    public Task SetAsync(Guid eventId, IReadOnlyList<EventSeatAvailability> seats, CancellationToken cancellationToken)
    {
        SetCalls++;
        Values[eventId] = seats;
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(Guid eventId, CancellationToken cancellationToken)
    {
        InvalidateCalls++;
        Values.Remove(eventId);
        return Task.CompletedTask;
    }
}
