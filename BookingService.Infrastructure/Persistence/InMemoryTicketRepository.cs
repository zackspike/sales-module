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
}
