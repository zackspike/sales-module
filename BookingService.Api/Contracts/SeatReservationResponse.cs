namespace BookingService.Api.Contracts;

/// <summary>
/// Seat locked for a buyer until <see cref="ExpiresAtUtc"/>.
/// </summary>
/// <param name="TicketId">Unique identifier of the reserved ticket (seat).</param>
/// <param name="EventId">Unique identifier of the event.</param>
/// <param name="SeatNumber">Designated seat number (e.g., 'A-1').</param>
/// <param name="Email">Email of the buyer holding the reservation.</param>
/// <param name="ExpiresAtUtc">UTC time when the reservation expires and other buyers can take the seat.</param>
public sealed record SeatReservationResponse(
    Guid TicketId,
    Guid EventId,
    string SeatNumber,
    string Email,
    DateTime ExpiresAtUtc);
