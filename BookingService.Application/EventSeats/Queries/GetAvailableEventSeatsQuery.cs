namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Query to list all available event seats for a specific event (SP-04 / APP-01).
/// </summary>
public sealed record GetAvailableEventSeatsQuery(Guid EventId);
