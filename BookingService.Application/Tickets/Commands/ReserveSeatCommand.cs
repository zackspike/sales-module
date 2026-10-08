namespace BookingService.Application.Tickets.Commands;

/// <summary>
/// Input of the seat reservation step: POST /events/{eventId}/tickets/{ticketId}/reserve.
/// Locks the seat for the buyer identified by <see cref="Email"/> during
/// <see cref="Domain.Tickets.SeatReservationPolicy.LockDuration"/>.
/// Text fields are nullable because they come straight from an untrusted request body.
/// </summary>
public sealed record ReserveSeatCommand(
    Guid EventId,
    Guid TicketId,
    string? FullName,
    string? Email);
