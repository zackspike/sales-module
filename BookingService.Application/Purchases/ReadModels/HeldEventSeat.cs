namespace BookingService.Application.Purchases.ReadModels;

/// <summary>
/// An event seat held by a purchase until its session expires.
/// </summary>
/// <param name="EventSeatId">Unique identifier of the held event seat.</param>
/// <param name="EventId">Unique identifier of the event.</param>
/// <param name="SeatNumber">Venue seat label (e.g., 'A-1').</param>
/// <param name="ExpiresAtUtc">
/// UTC time when the hold expires. Always the session's deadline: holding a seat does not extend it (ADR-001).
/// </param>
public sealed record HeldEventSeat(
    Guid EventSeatId,
    Guid EventId,
    string SeatNumber,
    DateTime ExpiresAtUtc);
