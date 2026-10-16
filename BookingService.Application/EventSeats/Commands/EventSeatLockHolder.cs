namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// The buyer identity stored in an event seat lock (see <see cref="Abstractions.IEventSeatLockStore"/>).
/// As for MVP it is the buyer's email, trimmed and case-insensitive, so the reservation and the purchase
/// requests produce the same identifier. In a future version it could be a user id or a reservation token.
/// </summary>
internal static class EventSeatLockHolder
{
    public static string From(string email) => email.Trim().ToLowerInvariant();
}
