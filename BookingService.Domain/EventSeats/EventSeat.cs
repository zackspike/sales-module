using BookingService.Domain.Tickets;

namespace BookingService.Domain.EventSeats;

public class EventSeat
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid VenueSeatId { get; private set; } // The physical seat associated. Value owned by VenueService.
    public Guid ZoneId { get; private set; } // The zone of the seat for a specific event. Value owned by VenueService.
    public SeatStatus Status { get; private set; }



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
            Status = SeatStatus.Available
        };
    }

    /// <summary>
    /// Sells this seat to the given customer. A seat can only be sold once.
    /// </summary>
    /// <exception cref="EventSeatAlreadySoldException"></exception>
    public Ticket Sell(string holderName, string holderEmail, DateTime soldAtUtc)
    {
        if (Status == SeatStatus.Sold) throw new EventSeatAlreadySoldException(Id);

        Status = SeatStatus.Sold;

        return Ticket.Issue(Id, holderName, holderEmail, soldAtUtc);
    }
}
