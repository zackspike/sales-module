namespace BookingService.Application.EventSeats.ReadModels;

/// <summary>
/// An event seat that can be bought, with the number of its venue seat (SP-04 / APP-01).
/// It does not account for seat locks: reserved seats are filtered out at read time.
/// </summary>
/// <param name="EventSeatId">Unique identifier of the event seat.</param>
/// <param name="SeatNumber">Venue seat label (e.g., 'A-1').</param>
public sealed record AvailableEventSeat(
    Guid EventSeatId,
    string SeatNumber);
