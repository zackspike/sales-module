namespace BookingService.Domain.Tickets;

/// <summary>
/// Represents the status of a seat in the booking system. Sold means there 
/// exist a <see cref="Ticket">Ticket</see> associated with this seat.</summary>
public enum SeatStatus
{
    Available,
    Sold
}
