using BookingService.Application.Repositories;
using BookingService.Domain;

namespace BookingService.Infrastructure.Repositories;

public class InMemoryTicketRepository : ITicketRepository
{
    private readonly List<Ticket> _tickets = new();
    private readonly Dictionary<Guid, Guid> _ticketIdsByIdempotencyKey = new();
    private readonly Lock _lock = new();

    public Ticket GetOrAdd(Guid idempotencyKey, Ticket ticket, out bool wasCreated)
    {
        lock (_lock)
        {
            // Both the lookup and the insert happen under the same lock, so two concurrent
            // requests carrying the same idempotency key can't each slip past the check and
            // create their own ticket.
            if (_ticketIdsByIdempotencyKey.TryGetValue(idempotencyKey, out var existingTicketId))
            {
                wasCreated = false;
                return _tickets.First(t => t.Id == existingTicketId);
            }

            _tickets.Add(ticket);
            _ticketIdsByIdempotencyKey[idempotencyKey] = ticket.Id;
            wasCreated = true;
            return ticket;
        }
    }

    public Ticket? GetById(Guid id)
    {
        lock (_lock)
        {
            return _tickets.FirstOrDefault(t => t.Id == id);
        }
    }

    public IReadOnlyCollection<Ticket> GetAll()
    {
        lock (_lock)
        {
            return _tickets.ToList();
        }
    }

    public Ticket Update(Ticket ticket)
    {
        lock (_lock)
        {
            var index = _tickets.FindIndex(t => t.Id == ticket.Id);
            if (index == -1)
            {
                throw new KeyNotFoundException($"Ticket with id '{ticket.Id}' was not found.");
            }

            _tickets[index] = ticket;
        }

        return ticket;
    }

    public bool Remove(Guid id)
    {
        lock (_lock)
        {
            var ticket = _tickets.FirstOrDefault(t => t.Id == id);
            return ticket is not null && _tickets.Remove(ticket);
        }
    }
}
