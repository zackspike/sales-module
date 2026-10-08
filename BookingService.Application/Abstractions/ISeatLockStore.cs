namespace BookingService.Application.Abstractions;

/// <summary>
/// Distributed, expiring seat locks (<c>ticket-id → holder</c>) that give one fan exclusive
/// right to buy a seat for a limited time. Every operation is atomic, so concurrent fans can't
/// both win the same seat. The holder is the buyer's normalized email.
/// </summary>
public interface ISeatLockStore
{
    /// <summary>
    /// Locks <paramref name="ticketId"/> for <paramref name="holder"/> during <paramref name="duration"/>.
    /// If the same holder already has it, the lock is renewed for another <paramref name="duration"/>.
    /// </summary>
    SeatLockResult TryAcquire(Guid ticketId, string holder, TimeSpan duration);

    /// <summary>
    /// Returns the holder of the lock of <paramref name="ticketId"/>, or null if it is not locked.
    /// </summary>
    string? GetHolder(Guid ticketId);

    /// <summary>
    /// Returns which of <paramref name="ticketIds"/> are currently locked by anyone.
    /// </summary>
    IReadOnlySet<Guid> GetLockedTicketIds(IReadOnlyCollection<Guid> ticketIds);

    /// <summary>
    /// Removes the lock of <paramref name="ticketId"/> only if it is held by <paramref name="holder"/>.
    /// </summary>
    bool Release(Guid ticketId, string holder);
}

public enum SeatLockResult
{
    /// <summary>The seat was free and is now locked for the holder.</summary>
    Acquired,

    /// <summary>The holder already had the lock; its expiration was extended.</summary>
    Renewed,

    /// <summary>Another holder has the lock; nothing changed.</summary>
    HeldByAnother
}
