namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Query to list all available tickets for a specific event (SP-04 / APP-01).
/// </summary>
public sealed record GetAvailableTicketsQuery(Guid EventId);
