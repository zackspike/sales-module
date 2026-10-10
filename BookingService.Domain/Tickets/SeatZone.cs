namespace BookingService.Domain.Tickets;

public record SeatZone
{
    public Guid Id { get; init; }
    public string EventId { get; init; } = null!; // zone is specific to an event, so we need to know which event this zone belongs to
    public decimal Price { get; init; }
}