namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Query to get the availability of a specific event seat in an event (SP-04 / APP-02).
/// </summary>
public sealed record GetEventSeatAvailabilityQuery(Guid EventId, Guid EventSeatId);
