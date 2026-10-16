using BookingService.Application.Abstractions;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeEventSeatLockStore : IEventSeatLockStore
{
    private readonly Dictionary<Guid, string> _holders = [];

    public IReadOnlyDictionary<Guid, string> Holders => _holders;

    public int AcquireCalls { get; private set; }

    public TimeSpan? LastDuration { get; private set; }

    public EventSeatLockResult TryAcquire(Guid eventSeatId, string holderIdentifier, TimeSpan duration)
    {
        AcquireCalls++;
        LastDuration = duration;
        if (!_holders.TryGetValue(eventSeatId, out var currentHolder))
        {
            _holders[eventSeatId] = holderIdentifier;
            return EventSeatLockResult.Acquired;
        }

        return currentHolder == holderIdentifier
            ? EventSeatLockResult.Renewed
            : EventSeatLockResult.HeldByAnother;
    }

    public string? GetHolderIdentifier(Guid eventSeatId) => _holders.GetValueOrDefault(eventSeatId);

    public IReadOnlySet<Guid> GetLockedEventSeatIds(IReadOnlyCollection<Guid> eventSeatIds) =>
        eventSeatIds.Where(_holders.ContainsKey).ToHashSet();

    public bool Release(Guid eventSeatId, string holderIdentifier)
    {
        return _holders.TryGetValue(eventSeatId, out var currentHolder)
            && currentHolder == holderIdentifier
            && _holders.Remove(eventSeatId);
    }

    public void ResetAcquireCalls() => AcquireCalls = 0;
}
