namespace BookingService.Domain;

public class Seat
{
    public Guid Id { get; private set; }
    public Guid ZoneId { get; private set; }
    public string SeatNumber { get; private set; } = string.Empty;
    public SeatStatus Status { get; private set; } = SeatStatus.Available; //by default, all seats are available
    public string HolderName { get; private set; } = string.Empty; // The name of the person who holds the seat. Not necesarily the same as the buyer of the ticket.
    public string HolderEmail { get; private set; } = string.Empty; // The email of the person who holds the seat. Not necesarily the same as the buyer of the ticket.
}