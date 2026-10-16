namespace BookingService.Application.EventSeats.ReadModels;

/// <summary>
/// Current availability of one event seat (SP-04 / APP-02).
/// </summary>
/// <param name="EventSeatId">Unique identifier of the event seat.</param>
/// <param name="SeatNumber">Venue seat label (e.g., 'A-1').</param>
/// <param name="Status">Whether the seat can be bought, is reserved by a fan or is sold.</param>
public sealed record EventSeatAvailability(
    Guid EventSeatId,
    string SeatNumber,
    EventSeatAvailabilityStatus Status);

/// <summary>
/// Availability as seen by a fan: the domain <see cref="Domain.EventSeats.EventSeatStatus"/>
/// combined with the seat lock.
/// </summary>
public enum EventSeatAvailabilityStatus
{
    /// <summary>The seat is not sold and nobody holds its lock.</summary>
    Available,

    /// <summary>The seat is not sold but a fan holds its lock.</summary>
    Reserved,

    /// <summary>The seat is sold.</summary>
    Sold
}
