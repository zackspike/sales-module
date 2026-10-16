using BookingService.Domain.Tickets;

namespace BookingService.Domain.EventSeats;

public class EventSeat
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid VenueSeatId { get; private set; } // The physical seat associated. Value owned by VenueService.
    public Guid ZoneId { get; private set; } // The zone of the seat for a specific event. Value owned by VenueService.

    /// <summary>
    /// Sale status of the seat. Temporary reservations are not part of it.
    /// </summary>
    public EventSeatStatus Status { get; private set; }

    // Only CreateAvailable (and EF Core, through reflection) can build an event seat.
    private EventSeat()
    {
    }

    /// <summary>
    /// Creates a new available event seat for the given event, venue seat and zone. The seat is initially available for purchase.
    /// </summary>
    public static EventSeat CreateAvailable(Guid eventId, Guid venueSeatId, Guid zoneId)
    {
        return new EventSeat
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            VenueSeatId = venueSeatId,
            ZoneId = zoneId,
            Status = EventSeatStatus.Available
        };
    }

    /// <summary>
    /// Sells this seat to the given customer. A seat can only be sold once.
    /// </summary>
    /// <exception cref="EventSeatAlreadySoldException"></exception>
    public Ticket Sell(string holderName, string holderEmail, DateTime soldAtUtc)
    {
        if (Status == EventSeatStatus.Sold)
        {
            throw new EventSeatAlreadySoldException(Id);
        }

        Status = EventSeatStatus.Sold;

        return Ticket.Issue(Id, holderName, holderEmail, soldAtUtc);
    }
}
