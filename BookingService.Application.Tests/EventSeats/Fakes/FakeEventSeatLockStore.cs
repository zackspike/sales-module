using BookingService.Application.Abstractions;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeEventSeatLockStore : IEventSeatLockStore
{
    private readonly Dictionary<Guid, string> _holders = [];

    public IReadOnlyDictionary<Guid, string> Holders => _holders;

    public int AcquireCalls { get; private set; }

    public TimeSpan? LastDuration { get; private set; }

    public Task<EventSeatLockResult> TryAcquireAsync(
        Guid eventSeatId,
        string holderIdentifier,
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        Task.FromResult(TryAcquire(eventSeatId, holderIdentifier, duration));

    public Task<string?> GetHolderIdentifierAsync(Guid eventSeatId, CancellationToken cancellationToken) =>
        Task.FromResult(GetHolderIdentifier(eventSeatId));

    public Task<IReadOnlySet<Guid>> GetLockedEventSeatIdsAsync(
        IReadOnlyCollection<Guid> eventSeatIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(eventSeatIds.Where(_holders.ContainsKey).ToHashSet());

    public Task<bool> ReleaseAsync(Guid eventSeatId, string holderIdentifier, CancellationToken cancellationToken)
    {
        return Task.FromResult(_holders.TryGetValue(eventSeatId, out var currentHolder)
            && currentHolder == holderIdentifier
            && _holders.Remove(eventSeatId));
    }

    // Synchronous helpers to arrange and assert locks in tests; the async members delegate to them.
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

    public void ResetAcquireCalls() => AcquireCalls = 0;
}
