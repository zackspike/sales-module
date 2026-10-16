namespace BookingService.Domain.EventSeats;

public record SeatZone
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; } // zone is specific to an event, so we need to know which event this zone belongs to
    public decimal Price { get; init; }
}
