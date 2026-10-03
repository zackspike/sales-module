using BookingService.Domain.Tickets;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Repository interface defining persistence operations for the <see cref="Ticket"/> aggregate root.
/// </summary>
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
    /// Finds a ticket previously purchased with <paramref name="idempotencyKey"/>, if any.
    /// Used to safely detect and replay idempotent requests.
    /// </summary>
    Ticket? GetByIdempotencyKey(Guid idempotencyKey);

    /// <summary>
    /// Persists changes to the mutated <paramref name="ticket"/> aggregate root.
    /// In persistent stores, enforces optimistic concurrency control.
    /// </summary>
    /// <exception cref="TicketAlreadySoldException">Thrown when an optimistic concurrency conflict occurs.</exception>
    void Update(Ticket ticket);
}
