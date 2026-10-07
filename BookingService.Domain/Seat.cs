namespace BookingService.Domain;

/// <summary>
/// Physical seat of a zone (e.g. "A-1"). Each seat generates exactly one ticket.
/// </summary>
public class Seat
{
    public Guid Id { get; set; }
    public Guid ZoneId { get; set; }
    public Zone? Zone { get; set; }
    public string SeatNumber { get; set; } = string.Empty;
}
