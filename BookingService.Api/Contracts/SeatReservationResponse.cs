namespace BookingService.Api.Contracts;

/// <summary>
/// Seat locked for a buyer until <see cref="ExpiresAtUtc"/>.
/// </summary>
/// <param name="SeatNumber">Designated seat number (e.g., 'A-1').</param>
/// <param name="ExpiresAtUtc">UTC time when the reservation expires and other buyers can take the seat.</param>
public sealed record SeatReservationResponse(
    string SeatNumber,
    DateTime ExpiresAtUtc);
