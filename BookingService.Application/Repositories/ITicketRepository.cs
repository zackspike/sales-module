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

    Ticket? GetById(Guid id);
    IReadOnlyCollection<Ticket> GetAll();
    Ticket Update(Ticket ticket);
    bool Remove(Guid id);
}
