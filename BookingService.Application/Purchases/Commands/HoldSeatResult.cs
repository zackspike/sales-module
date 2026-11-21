using BookingService.Application.Purchases.ReadModels;

namespace BookingService.Application.Purchases.Commands;

public enum HoldSeatStatus
{
    /// <summary>The purchase holds the seat, newly or already (HTTP 200).</summary>
    Held,

    /// <summary>A required field is missing or malformed (HTTP 400, VALIDATION_FAILED).</summary>
    Invalid,

    /// <summary>The purchase session is missing or expired (HTTP 401, SESSION_EXPIRED).</summary>
    SessionExpired,

    /// <summary>The target event does not exist (HTTP 404, EVENT_NOT_FOUND).</summary>
    EventNotFound,

    /// <summary>The event seat does not exist in the event (HTTP 404, SEAT_NOT_FOUND).</summary>
    EventSeatNotFound,

    /// <summary>The seat was already sold (HTTP 409, SEAT_ALREADY_SOLD).</summary>
    AlreadySold,

    /// <summary>Another purchase holds the seat (HTTP 409, SEAT_HELD_BY_ANOTHER_PURCHASE).</summary>
    HeldByAnotherPurchase,

    /// <summary>The purchase already holds as many seats as it may (HTTP 409, SEAT_LIMIT_REACHED).</summary>
    SeatLimitReached
}

/// <summary>
/// Outcome of <see cref="HoldSeatHandler"/>. <see cref="Seat"/> is set only for
/// <see cref="HoldSeatStatus.Held"/>; <see cref="Errors"/> only for
/// <see cref="HoldSeatStatus.Invalid"/>, keyed by JSON field name.
/// </summary>
public sealed record HoldSeatResult(
    HoldSeatStatus Status,
    HeldEventSeat? Seat,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public static HoldSeatResult Held(HeldEventSeat seat) =>
        new(HoldSeatStatus.Held, seat, NoErrors);

    public static HoldSeatResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(HoldSeatStatus.Invalid, null, errors);

    public static HoldSeatResult Failed(HoldSeatStatus status) =>
        new(status, null, NoErrors);
}
