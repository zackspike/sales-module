namespace BookingService.Domain;

/// <summary>
/// Represents an event in the booking domain (SP-03 / ALIGN-01).
/// Contains the shared event model agreed with EventService and VenueService.
/// </summary>
public class Event
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public Guid VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int TotalSeats { get; set; }
}
