namespace BookingService.Application.EventSeats.ReadModels;

/// <summary>
/// An event seat locked for a buyer until <see cref="ExpiresAtUtc"/>.
/// </summary>
/// <param name="EventSeatId">Unique identifier of the reserved event seat.</param>
/// <param name="EventId">Unique identifier of the event.</param>
/// <param name="SeatNumber">Venue seat label (e.g., 'A-1').</param>
/// <param name="ExpiresAtUtc">UTC time when the reservation expires and other buyers can take the seat.</param>
/// <param name="BuyerToken">Buyer's secret; it must be sent back to release or purchase the seat.</param>
public sealed record ReservedEventSeat(
    Guid EventSeatId,
    Guid EventId,
    string SeatNumber,
    DateTime ExpiresAtUtc,
    string BuyerToken);
