using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Tickets;

namespace BookingService.Infrastructure.Persistence;

public sealed class InMemoryEventSeatRepository : IEventSeatRepository
{
    // Event seats grouped by event (eventId -> eventSeatId -> seat) plus a flat
    // seatId -> seat index, tickets by id, and an idempotency key -> ticket id index.
    private readonly Dictionary<Guid, Dictionary<Guid, EventSeat>> _seatsByEvent = new();
    private readonly Dictionary<Guid, EventSeat> _seatsById = new();
    private readonly Dictionary<Guid, Ticket> _ticketsById = new();
    private readonly Dictionary<Guid, Guid> _ticketIdsByIdempotencyKey = new();
    private readonly Lock _lock = new();

    public InMemoryEventSeatRepository(bool seedDefaultInventory = false)
    {
        if (seedDefaultInventory)
        {
            SeedDefaultInventory();
        }
    }

    private void SeedDefaultInventory()
    {
        var defaultEvent = InMemoryEventCatalog.DefaultEvent;
        var initialSeats = EventInventoryFactory.GenerateEventSeats(defaultEvent.Id, InMemoryVenueSeatCatalog.DefaultVenueSeats);

        lock (_lock)
        {
            foreach (var seat in initialSeats)
            {
                StoreSeat(seat);
            }
        }
    }

    public IReadOnlyCollection<EventSeat> GetByEvent(Guid eventId)
    {
        lock (_lock)
        {
            return _seatsByEvent.TryGetValue(eventId, out var eventSeats)
                ? eventSeats.Values.ToList()
                : [];
        }
    }

    public EventSeat? GetById(Guid eventId, Guid eventSeatId)
    {
        lock (_lock)
        {
            return _seatsByEvent.TryGetValue(eventId, out var eventSeats)
                && eventSeats.TryGetValue(eventSeatId, out var eventSeat)
                    ? eventSeat
                    : null;
        }
    }

    public EventSeatSale? GetSaleByIdempotencyKey(Guid idempotencyKey, Guid eventId, Guid eventSeatId)
    {
        lock (_lock)
        {
            if (!_ticketIdsByIdempotencyKey.TryGetValue(idempotencyKey, out var ticketId))
            {
                return null;
            }

            if (!_ticketsById.TryGetValue(ticketId, out var ticket))
            {
                return null;
            }

            return _seatsById.TryGetValue(eventSeatId, out var seat) && seat.EventId == eventId
                ? new EventSeatSale(seat, ticket)
                : null;
        }
    }

    public EventSeatSaleOutcome SellOnce(Guid idempotencyKey, Guid eventId, Guid eventSeatId, Func<EventSeat, Ticket> sell)
    {
        lock (_lock)
        {
            // The key check, the availability check done by `sell` and the key registration
            // share one critical section: of two concurrent buyers of the same seat only one can
            // see it available, and a retried request can't buy a second seat.
            if (_ticketIdsByIdempotencyKey.TryGetValue(idempotencyKey, out var existingTicketId))
            {
                var existingTicket = _ticketsById[existingTicketId];
                return new EventSeatSaleOutcome(ToSale(existingTicket), Replayed: true);
            }

            var seat = _seatsByEvent.TryGetValue(eventId, out var eventSeats)
                ? eventSeats.GetValueOrDefault(eventSeatId)
                : null;
            if (seat is null)
            {
                return new EventSeatSaleOutcome(null, Replayed: false);
            }

            var ticket = sell(seat);
            _ticketsById[ticket.Id] = ticket;
            if (idempotencyKey != Guid.Empty)
            {
                _ticketIdsByIdempotencyKey[idempotencyKey] = ticket.Id;
            }

            return new EventSeatSaleOutcome(new EventSeatSale(seat, ticket), Replayed: false);
        }
    }

    // The helpers below assume the caller holds _lock.

    private EventSeatSale ToSale(Ticket ticket)
    {
        var seat = _seatsById[ticket.EventSeatId];
        return new EventSeatSale(seat, ticket);
    }

    private void StoreSeat(EventSeat seat)
    {
        if (!_seatsByEvent.TryGetValue(seat.EventId, out var eventSeats))
        {
            eventSeats = new Dictionary<Guid, EventSeat>();
            _seatsByEvent[seat.EventId] = eventSeats;
        }

        eventSeats[seat.Id] = seat;
        _seatsById[seat.Id] = seat;
    }
}
