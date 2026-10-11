namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Query to check the availability of a specific event seat in an event (SP-04 / APP-02).
/// </summary>
public sealed record CheckEventSeatAvailabilityQuery(Guid EventId, Guid EventSeatId);
