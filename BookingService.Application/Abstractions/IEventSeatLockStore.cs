namespace BookingService.Application.Abstractions;

/// <summary>
/// Distributed, expiring event seat locks (<c>event-seat-id → holderIdentifier</c>) that give one fan exclusive
/// right to buy a seat for a limited time. Every operation is atomic, so concurrent fans can't both win the same seat.
/// As for MVP, the holderIdentifier is the fan's email address. In a future version, it could be a user id or session id.
/// </summary>
public interface IEventSeatLockStore
{
    /// <summary>
    /// Locks <paramref name="eventSeatId"/> for <paramref name="holderIdentifier"/> during <paramref name="duration"/>.
    /// If the same holder already has it, the lock is renewed for another <paramref name="duration"/>.
    /// </summary>
    Task<EventSeatLockResult> TryAcquireAsync(
        Guid eventSeatId,
        string holderIdentifier,
        TimeSpan duration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the holder of the lock of <paramref name="eventSeatId"/>, or null if it is not locked.
    /// </summary>
    Task<string?> GetHolderIdentifierAsync(Guid eventSeatId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns which of <paramref name="eventSeatIds"/> are currently locked by anyone.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetLockedEventSeatIdsAsync(
        IReadOnlyCollection<Guid> eventSeatIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the lock of <paramref name="eventSeatId"/> only if it is held by <paramref name="holderIdentifier"/>.
    /// </summary>
    Task<bool> ReleaseAsync(Guid eventSeatId, string holderIdentifier, CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of <see cref="IEventSeatLockStore.TryAcquireAsync"/>.
/// </summary>
public enum EventSeatLockResult
{
    /// <summary>The event seat was free and is now locked for the holder.</summary>
    Acquired,

    /// <summary>The holder already had the lock; its expiration was extended.</summary>
    Renewed,

    /// <summary>Another holder has the lock; nothing changed.</summary>
    HeldByAnother
}
