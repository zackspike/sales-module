namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// The identity stored in an event seat lock (see <see cref="Abstractions.IEventSeatLockStore"/>) once a
/// purchase session owns the hold: the purchase id on its own. The buyer's email is no longer part of it
/// because it lives in the session, and the id is only known to the browser holding the purchase cookie,
/// so knowing someone's email is still not enough to touch their seats.
/// </summary>
internal static class PurchaseSeatLockHolder
{
    public static string Of(Guid purchaseId) => purchaseId.ToString();
}
