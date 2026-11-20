namespace BookingService.Domain.EventSeats;

/// <summary>
/// Rules of the temporary seat reservation taken when a fan clicks "comprar".
/// </summary>
[Obsolete("Held seats expire with the purchase session (Purchase.ExpiresAtUtc), whose duration is configurable. Remove when Application moves to Purchase.")]
public static class EventSeatReservationPolicy
{
    /// <summary>
    /// How long a seat stays reserved for the fan who reserved it before others can take it.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);
}
