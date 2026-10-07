namespace BookingService.Domain;

/// <summary>
/// Priced area of an event that groups its seats (Event 1-N Zone 1-N Seat).
/// </summary>
public class Zone
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public decimal Price { get; set; }
    public List<Seat> Seats { get; set; } = [];
}
