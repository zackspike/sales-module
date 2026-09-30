using BookingService.Domain;

namespace BookingService.Application.Repositories;

public interface ITicketRepository
{
    /// <summary>
    /// Creates <paramref name="ticket"/> the first time <paramref name="idempotencyKey"/> is seen.
    /// Any later call with the same key returns the ticket created on that first call instead of
    /// creating a new one, so retried/duplicated "create ticket" requests are safe to repeat.
    /// This is the only way to create a ticket; there is no separate non-idempotent Add.
    /// </summary>
    Ticket GetOrAdd(Guid idempotencyKey, Ticket ticket, out bool wasCreated);

    /// <summary>
    /// Runs <paramref name="purchase"/> on seat <paramref name="ticketId"/> of event
    /// <paramref name="eventId"/> at most once per <paramref name="idempotencyKey"/>.
    /// The key lookup, the seat lookup, the purchase and the key registration happen
    /// atomically, so two concurrent buyers can't both acquire the same seat.
    /// If <paramref name="purchase"/> throws, the key is not registered and the exception propagates.
    /// </summary>
    TicketPurchaseOutcome PurchaseOnce(Guid idempotencyKey, Guid eventId, Guid ticketId, Action<Ticket> purchase);

    Ticket? GetById(Guid id);
    IReadOnlyCollection<Ticket> GetAll();
    Ticket Update(Ticket ticket);
    bool Remove(Guid id);
}

/// <summary>
/// Result of <see cref="ITicketRepository.PurchaseOnce"/>. <see cref="Ticket"/> is null when the
/// seat does not exist in the event. When the key was already used, <see cref="Replayed"/> is true
/// and <see cref="Ticket"/> is the ticket bought with it (which may be a different seat).
/// </summary>
public sealed record TicketPurchaseOutcome(Ticket? Ticket, bool Replayed);
