namespace BookingService.Domain;

/// <summary>
/// Rules of the temporary seat reservation taken when a fan clicks "comprar".
/// </summary>
public static class SeatReservationPolicy
{
    /// <summary>
    /// How long a seat stays locked for the fan who reserved it before others can take it.
    /// </summary>
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);
}
