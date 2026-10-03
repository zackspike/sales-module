using BookingService.Application.Abstractions;
using BookingService.Domain.Events;
using BookingService.Domain.Tickets;

namespace BookingService.Infrastructure.Persistence;

public class InMemoryTicketRepository : ITicketRepository
{
    private readonly Dictionary<Guid, Dictionary<Guid, Ticket>> _ticketsByEvent = new();
    private readonly Dictionary<Guid, Guid> _eventIdsByTicketId = new();
    private readonly Dictionary<Guid, Guid> _ticketIdsByIdempotencyKey = new();
    private readonly Lock _lock = new();

    public InMemoryTicketRepository(bool seedDefaultInventory = false)
    {
        if (seedDefaultInventory)
        {
            SeedDefaultInventory();
        }
    }

    private void SeedDefaultInventory()
    {
        var defaultEvent = InMemoryEventCatalog.DefaultEvent;
        var initialSeats = EventInventoryFactory.CreateInitialInventory(defaultEvent);
        AddRange(defaultEvent.Id, initialSeats);
    }

    public void AddRange(Guid eventId, IEnumerable<Ticket> tickets)
    {
        var batch = tickets.ToList();

        lock (_lock)
        {
            var batchIds = new HashSet<Guid>();
            foreach (var ticket in batch)
            {
                if (ticket.EventId != eventId)
                {
                    throw new ArgumentException(
                        $"Ticket '{ticket.Id}' belongs to event '{ticket.EventId}', not '{eventId}'.",
                        nameof(tickets));
                }

                if (_eventIdsByTicketId.ContainsKey(ticket.Id) || !batchIds.Add(ticket.Id))
                {
                    throw new ArgumentException($"Ticket with id '{ticket.Id}' already exists.", nameof(tickets));
                }
            }

            foreach (var ticket in batch)
            {
                Store(ticket);
            }
        }
    }

    public IReadOnlyCollection<Ticket> GetByEvent(Guid eventId)
    {
        lock (_lock)
        {
            return _ticketsByEvent.TryGetValue(eventId, out var eventTickets)
                ? eventTickets.Values.ToList()
                : [];
        }
    }

    public Ticket? GetById(Guid eventId, Guid ticketId)
    {
        lock (_lock)
        {
            return _ticketsByEvent.TryGetValue(eventId, out var eventTickets)
                && eventTickets.TryGetValue(ticketId, out var ticket)
                    ? ticket
                    : null;
        }
    }

    public Ticket? GetByIdempotencyKey(Guid idempotencyKey)
    {
        if (idempotencyKey == Guid.Empty)
        {
            return null;
        }

        lock (_lock)
        {
            return _ticketIdsByIdempotencyKey.TryGetValue(idempotencyKey, out var ticketId)
                ? FindById(ticketId)
                : null;
        }
    }

    public void Update(Ticket ticket)
    {
        lock (_lock)
        {
            if (!_eventIdsByTicketId.ContainsKey(ticket.Id))
            {
                throw new KeyNotFoundException($"Ticket with id '{ticket.Id}' was not found.");
            }

            Store(ticket);

            if (ticket.IdempotencyKey.HasValue && ticket.IdempotencyKey.Value != Guid.Empty)
            {
                _ticketIdsByIdempotencyKey[ticket.IdempotencyKey.Value] = ticket.Id;
            }
        }
    }

    private Ticket? FindById(Guid id)
    {
        return _eventIdsByTicketId.TryGetValue(id, out var eventId)
            ? _ticketsByEvent[eventId][id]
            : null;
    }

    private void Store(Ticket ticket)
    {
        if (!_ticketsByEvent.TryGetValue(ticket.EventId, out var eventTickets))
        {
            eventTickets = new Dictionary<Guid, Ticket>();
            _ticketsByEvent[ticket.EventId] = eventTickets;
        }

        eventTickets[ticket.Id] = ticket;
        _eventIdsByTicketId[ticket.Id] = ticket.EventId;
    }
}
