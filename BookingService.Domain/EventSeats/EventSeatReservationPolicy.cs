namespace BookingService.Domain.EventSeats;

/// <summary>
/// Rules of the temporary seat reservation taken when a fan clicks "comprar".
/// </summary>
public static class EventSeatReservationPolicy
{
    /// <summary>
    /// How long a seat stays reserved for the fan who reserved it before others can take it.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);
}
