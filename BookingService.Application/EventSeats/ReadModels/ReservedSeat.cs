namespace BookingService.Application.EventSeats.ReadModels;

/// <summary>
/// An event seat locked for a buyer until <see cref="ExpiresAtUtc"/>.
/// </summary>
/// <param name="EventSeatId">Unique identifier of the reserved event seat.</param>
/// <param name="EventId">Unique identifier of the event.</param>
/// <param name="SeatNumber">Venue seat label (e.g., 'A-1').</param>
/// <param name="ExpiresAtUtc">UTC time when the reservation expires and other buyers can take the seat.</param>
public sealed record ReservedSeat(
    Guid EventSeatId,
    Guid EventId,
    string SeatNumber,
    DateTime ExpiresAtUtc);
