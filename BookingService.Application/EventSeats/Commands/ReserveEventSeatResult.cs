using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.EventSeats.Commands;

public enum ReserveEventSeatStatus
{
    /// <summary>The seat is reserved for the buyer, newly or renewed (HTTP 200).</summary>
    Reserved,

    /// <summary>A required field is missing or malformed (HTTP 400).</summary>
    Invalid,

    /// <summary>The target event does not exist (HTTP 404).</summary>
    EventNotFound,

    /// <summary>The event seat does not exist in the event (HTTP 404).</summary>
    EventSeatNotFound,

    /// <summary>The seat was already sold (HTTP 409).</summary>
    AlreadySold,

    /// <summary>Another buyer has the seat reserved (HTTP 409).</summary>
    ReservedByAnotherBuyer
}

/// <summary>
/// Outcome of <see cref="ReserveEventSeatHandler"/>. <see cref="Seat"/> is set only for
/// <see cref="ReserveEventSeatStatus.Reserved"/>; <see cref="Errors"/> only for
/// <see cref="ReserveEventSeatStatus.Invalid"/>.
/// </summary>
public sealed record ReserveEventSeatResult(
    ReserveEventSeatStatus Status,
    ReservedEventSeat? Seat,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public static ReserveEventSeatResult Reserved(ReservedEventSeat seat) =>
        new(ReserveEventSeatStatus.Reserved, seat, NoErrors);

    public static ReserveEventSeatResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(ReserveEventSeatStatus.Invalid, null, errors);

    public static ReserveEventSeatResult Failed(ReserveEventSeatStatus status) =>
        new(status, null, NoErrors);
}
