using System.Security.Cryptography;

namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// The buyer identity stored in an event seat lock (see <see cref="Abstractions.IEventSeatLockStore"/>):
/// <c>{buyerToken}:{email}</c>. The buyer token is a random secret issued by <see cref="ReserveEventSeatHandler"/>
/// and kept by the buyer's browser, so knowing someone's email is not enough to release or buy their reservation.
/// The email (trimmed, case-insensitive) keeps the rule that the seat is bought with the email it was reserved with.
/// </summary>
internal static class EventSeatLockHolder
{
    // A missing token yields ":email", which never matches a lock: reserve always uses a non-empty token.
    public static string From(string email, string? buyerToken) => $"{buyerToken}:{email.Trim().ToLowerInvariant()}";

    public static string NewToken() => RandomNumberGenerator.GetHexString(64, lowercase: true);
}
