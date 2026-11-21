namespace BookingService.Application.Purchases;

/// <summary>
/// Rules of the purchase session, from the <c>Purchase</c> configuration section. Both are
/// configurable per environment.
/// </summary>
public sealed class PurchaseSessionOptions
{
    /// <summary>
    /// How long a purchase session lives (<c>Purchase:SessionDuration</c>). Held seats expire with it.
    /// </summary>
    public TimeSpan SessionDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Most seats one purchase may hold (<c>Purchase:MaxSeats</c>), so a single buyer cannot hold a
    /// whole event.
    /// </summary>
    public int MaxSeats { get; init; } = 3;
}
