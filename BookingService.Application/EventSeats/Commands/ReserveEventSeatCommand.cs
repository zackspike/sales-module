namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Input of the seat reservation step: POST /events/{eventId}/tickets/{ticketId}/reserve,
/// where the route's ticketId is the event seat id.
/// Reserves the seat for the buyer identified by <see cref="Email"/> during
/// <see cref="Domain.EventSeats.EventSeatReservationPolicy.Duration"/>.
/// </summary>
public sealed record ReserveEventSeatCommand(
    Guid EventId,
    Guid EventSeatId,
    string FullName,
    string Email);
