namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Query to check the availability of a specific seat in an event (SP-04 / APP-02).
/// </summary>
public sealed record CheckTicketAvailabilityQuery(Guid EventId, Guid TicketId);
