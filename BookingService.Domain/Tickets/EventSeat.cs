using BookingService.Domain.Exceptions;

namespace BookingService.Domain.Tickets;

public class EventSeat
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid VenueSeatId { get; private set; } // The physical seat associated. Value owned by VenueService.
    public SeatStatus Status { get; private set; }



    /// <summary>
    /// Creates a new available event seat for the given event and venue seat. The seat is initially available for purchase.
    /// </summary>
    public static EventSeat CreateAvailable(Guid eventId, Guid venueSeatId)
    {
        return new EventSeat
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            VenueSeatId = venueSeatId,
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