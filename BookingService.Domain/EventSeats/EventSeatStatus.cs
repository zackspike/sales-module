namespace BookingService.Domain.EventSeats;

/// <summary>
/// Sale status of an <see cref="EventSeat"/>, stored with it. Sold means there
/// exists a <see cref="Tickets.Ticket">Ticket</see> associated with this seat.
/// Temporary reservations (seat locks) are not part of this status.
/// </summary>
public enum EventSeatStatus
{
    /// <summary>The seat has not been sold yet.</summary>
    Available,

    /// <summary>The seat was sold and its ticket issued.</summary>
    Sold
}
