using BookingService.Domain.Tickets;

namespace BookingService.Application.Abstractions;

public interface ITicketRepository
{
    /// <summary>
    /// Adds the ticket inventory (seats) of event <paramref name="eventId"/> (SP-03 / INF-01).
    /// Every ticket must belong to that event and have an id not already stored; otherwise
    /// nothing is added and an <see cref="ArgumentException"/> is thrown.
    /// </summary>
    void AddRange(Guid eventId, IEnumerable<Ticket> tickets);

    /// <summary>
    /// Returns a snapshot of the tickets of event <paramref name="eventId"/>; empty if the event has none.
    /// </summary>
    IReadOnlyCollection<Ticket> GetByEvent(Guid eventId);

    /// <summary>
    /// Returns ticket <paramref name="ticketId"/> only if it belongs to event <paramref name="eventId"/>.
    /// </summary>
    Ticket? GetById(Guid eventId, Guid ticketId);

    /// <summary>
    /// Runs <paramref name="purchase"/> on seat <paramref name="ticketId"/> of event
    /// <paramref name="eventId"/> at most once per <paramref name="idempotencyKey"/>.
    /// The key lookup, the seat lookup, the purchase and the key registration happen
    /// atomically, so two concurrent buyers can't both acquire the same seat.
    /// If <paramref name="purchase"/> throws, the key is not registered and the exception propagates.
    /// </summary>
    TicketPurchaseOutcome PurchaseOnce(Guid idempotencyKey, Guid eventId, Guid ticketId, Action<Ticket> purchase);
}

/// <summary>
/// Result of <see cref="ITicketRepository.PurchaseOnce"/>. <see cref="Ticket"/> is null when the
/// seat does not exist in the event. When the key was already used, <see cref="Replayed"/> is true
/// and <see cref="Ticket"/> is the ticket bought with it (which may be a different seat).
/// </summary>
public sealed record TicketPurchaseOutcome(Ticket? Ticket, bool Replayed);
