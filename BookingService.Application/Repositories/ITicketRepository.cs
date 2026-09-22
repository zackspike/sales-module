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

    Ticket? GetById(Guid id);
    IReadOnlyCollection<Ticket> GetAll();
    Ticket Update(Ticket ticket);
    bool Remove(Guid id);
}
