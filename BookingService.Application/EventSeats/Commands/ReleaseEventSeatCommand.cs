namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Input of POST /events/{eventId}/tickets/{ticketId}/release, where the route's ticketId is the event seat id.
/// Releases the reservation held by the buyer identified by <see cref="Email"/>.
/// </summary>
public sealed record ReleaseEventSeatCommand(
    Guid EventId,
    Guid EventSeatId,
    string Email);
