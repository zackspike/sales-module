namespace BookingService.Application.Caching;

/// <summary>
/// Distributed, expiring seat locks (<c>ticket-id → user-id</c>) that give one fan exclusive
/// right to buy a seat for a limited time. Every operation is atomic, so concurrent fans can't
/// both win the same seat.
/// </summary>
public interface ISeatLockStore
{
    /// <summary>
    /// Locks <paramref name="ticketId"/> for <paramref name="userId"/> during <paramref name="duration"/>.
    /// If the same user already holds it, the lock is renewed for another <paramref name="duration"/>.
    /// </summary>
    Task<SeatLockResult> TryAcquireAsync(
        Guid ticketId,
        Guid userId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user holding the lock of <paramref name="ticketId"/>, or null if it is not locked.
    /// </summary>
    Task<Guid?> GetHolderAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns which of <paramref name="ticketIds"/> are currently locked by anyone.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetLockedTicketIdsAsync(
        IReadOnlyCollection<Guid> ticketIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the lock of <paramref name="ticketId"/> only if it is held by <paramref name="userId"/>.
    /// </summary>
    Task<bool> ReleaseAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken = default);
}

public enum SeatLockResult
{
    /// <summary>The seat was free and is now locked for the user.</summary>
    Acquired,

    /// <summary>The user already held the lock; its expiration was extended.</summary>
    Renewed,

    /// <summary>Another user holds the lock; nothing changed.</summary>
    HeldByAnotherUser
}
