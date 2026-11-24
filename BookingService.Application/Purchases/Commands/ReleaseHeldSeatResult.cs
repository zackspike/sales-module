namespace BookingService.Application.Purchases.Commands;

public enum ReleaseHeldSeatStatus
{
    /// <summary>The purchase no longer holds the seat: it was released or was not held (HTTP 204).</summary>
    Released,

    /// <summary>The purchase session is missing or expired (HTTP 401, SESSION_EXPIRED).</summary>
    SessionExpired,

    /// <summary>The target event does not exist (HTTP 404, EVENT_NOT_FOUND).</summary>
    EventNotFound,

    /// <summary>The event seat does not exist in the event (HTTP 404, SEAT_NOT_FOUND).</summary>
    EventSeatNotFound,

    /// <summary>The seat was already sold (HTTP 409, SEAT_ALREADY_SOLD).</summary>
    AlreadySold,

    /// <summary>Another purchase holds the seat (HTTP 409, SEAT_HELD_BY_ANOTHER_PURCHASE).</summary>
    HeldByAnotherPurchase
}

/// <summary>
/// Outcome of <see cref="ReleaseHeldSeatHandler"/>.
/// </summary>
public sealed record ReleaseHeldSeatResult(ReleaseHeldSeatStatus Status)
{
    public static ReleaseHeldSeatResult Of(ReleaseHeldSeatStatus status) => new(status);
}
