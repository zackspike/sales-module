namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Input of the seat reservation step: POST /events/{eventId}/tickets/{ticketId}/reserve,
/// where the route's ticketId is the event seat id.
/// Locks the seat for the buyer identified by <see cref="Email"/> during
/// <see cref="Domain.EventSeats.EventSeatReservationPolicy.LockDuration"/>.
/// Text fields are nullable because they come straight from an untrusted request body.
/// </summary>
public sealed record ReserveSeatCommand(
    Guid EventId,
    Guid EventSeatId,
    string? FullName,
    string? Email);
