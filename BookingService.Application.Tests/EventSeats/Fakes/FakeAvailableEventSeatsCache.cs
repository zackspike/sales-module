using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeAvailableEventSeatsCache : IAvailableEventSeatsCache
{
    public Dictionary<Guid, IReadOnlyList<AvailableEventSeat>> Values { get; } = [];

    public int SetCalls { get; private set; }

    public int InvalidateCalls { get; private set; }

    public Task<IReadOnlyList<AvailableEventSeat>?> GetAsync(Guid eventId, CancellationToken cancellationToken) =>
        Task.FromResult(Values.GetValueOrDefault(eventId));

    public Task SetAsync(Guid eventId, IReadOnlyList<AvailableEventSeat> seats, CancellationToken cancellationToken)
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
