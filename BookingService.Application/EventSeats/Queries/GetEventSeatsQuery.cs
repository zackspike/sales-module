namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Query to list all event seats of a specific event with their availability (SP-04 / APP-01).
/// </summary>
public sealed record GetEventSeatsQuery(Guid EventId);
