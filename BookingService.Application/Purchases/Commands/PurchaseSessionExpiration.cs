using BookingService.Application.Abstractions;
using BookingService.Domain.Purchases;

namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// What every use case does when it finds an expired purchase session.
/// </summary>
internal static class PurchaseSessionExpiration
{
    /// <summary>
    /// Frees the seats the expired <paramref name="purchase"/> was holding, so they are available again
    /// without anyone asking for it. Only the locks this purchase still holds are removed.
    /// </summary>
    // The seat locks carry the session's own deadline, so they normally expire with it on their own.
    // This covers the slack between the two, and the buyer who closes the browser and never calls back.
    // ponytail: a loop, not a batch: a purchase holds at most PurchaseSessionOptions.MaxSeats seats.
    public static async Task ReleaseHeldSeatsAsync(
        PurchaseSession purchase,
        IEventSeatLockStore eventSeatLocks,
        CancellationToken cancellationToken)
    {
        var holder = PurchaseSeatLockHolder.Of(purchase.Id);

        foreach (var eventSeatId in purchase.HeldEventSeatIds)
        {
            await eventSeatLocks.ReleaseAsync(eventSeatId, holder, cancellationToken);
        }
    }
}
