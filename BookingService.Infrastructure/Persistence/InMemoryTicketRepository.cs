using BookingService.Application.Abstractions;
using BookingService.Domain.Events;
using BookingService.Domain.Tickets;

namespace BookingService.Infrastructure.Persistence;

public class InMemoryTicketRepository : ITicketRepository
{
    // Tickets grouped by event (eventId -> ticketId -> ticket), plus a ticketId -> eventId index
    // so lookups by ticket id alone stay O(1).
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

    public void SeedDefaultInventory()
    {
        var defaultEvent = InMemoryEventCatalog.DefaultEvent;
        var initialSeats = EventInventoryFactory.CreateInitialInventory(defaultEvent);
        AddRange(defaultEvent.Id, initialSeats);
    }

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
                return FindById(existingTicketId)
                    ?? throw new InvalidOperationException($"Ticket with id '{existingTicketId}' was removed.");
            }

            Store(ticket);
            _ticketIdsByIdempotencyKey[idempotencyKey] = ticket.Id;
            wasCreated = true;
            return ticket;
        }
    }

    public void AddRange(Guid eventId, IEnumerable<Ticket> tickets)
    {
        var batch = tickets.ToList();

        lock (_lock)
        {
            // Validate the whole batch before storing anything, so a bad ticket leaves the
            // event inventory untouched.
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

    public TicketPurchaseOutcome PurchaseOnce(Guid idempotencyKey, Guid eventId, Guid ticketId, Action<Ticket> purchase)
    {
        lock (_lock)
        {
            // The key check, the availability check done by `purchase` and the key registration
            // share one critical section: of two concurrent buyers of the same seat only one can
            // see it available, and a retried request can't buy a second seat.
            if (_ticketIdsByIdempotencyKey.TryGetValue(idempotencyKey, out var existingTicketId))
            {
                return new TicketPurchaseOutcome(FindById(existingTicketId)!, Replayed: true);
            }

            var ticket = _ticketsByEvent.TryGetValue(eventId, out var eventTickets)
                ? eventTickets.GetValueOrDefault(ticketId)
                : null;
            if (ticket is null)
            {
                return new TicketPurchaseOutcome(null, Replayed: false);
            }

            purchase(ticket);
            _ticketIdsByIdempotencyKey[idempotencyKey] = ticket.Id;
            return new TicketPurchaseOutcome(ticket, Replayed: false);
        }
    }

    public Ticket? GetById(Guid id)
    {
        lock (_lock)
        {
            return FindById(id);
        }
    }

    public IReadOnlyCollection<Ticket> GetAll()
    {
        lock (_lock)
        {
            return _ticketsByEvent.Values.SelectMany(eventTickets => eventTickets.Values).ToList();
        }
    }

    public Ticket Update(Ticket ticket)
    {
        lock (_lock)
        {
            if (!_eventIdsByTicketId.ContainsKey(ticket.Id))
            {
                throw new KeyNotFoundException($"Ticket with id '{ticket.Id}' was not found.");
            }

            // Removing first keeps the grouping right if the ticket's EventId changed.
            RemoveStored(ticket.Id);
            Store(ticket);
        }

        return ticket;
    }

    public bool Remove(Guid id)
    {
        lock (_lock)
        {
            return RemoveStored(id);
        }
    }

    // The helpers below assume the caller holds _lock.

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

    private bool RemoveStored(Guid id)
    {
        if (!_eventIdsByTicketId.Remove(id, out var eventId))
        {
            return false;
        }

        var eventTickets = _ticketsByEvent[eventId];
        eventTickets.Remove(id);
        if (eventTickets.Count == 0)
        {
            _ticketsByEvent.Remove(eventId);
        }

        return true;
    }
}
