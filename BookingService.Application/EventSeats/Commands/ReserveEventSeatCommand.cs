namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Input of the seat reservation step: POST /events/{eventId}/tickets/{ticketId}/reserve,
/// where the route's ticketId is the event seat id.
/// Reserves the seat for the buyer identified by <see cref="Email"/> during
/// <see cref="Domain.EventSeats.EventSeatReservationPolicy.Duration"/>.
/// <see cref="BuyerToken"/> is the buyer's secret from a previous reservation, or null to issue a new one.
/// </summary>
public sealed record ReserveEventSeatCommand(
    Guid EventId,
    Guid EventSeatId,
    string Email,
    string? BuyerToken);
