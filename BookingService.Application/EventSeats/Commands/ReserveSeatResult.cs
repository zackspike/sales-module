using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.EventSeats.Commands;

public enum ReserveSeatStatus
{
    /// <summary>The seat is locked for the buyer, newly or renewed (HTTP 200).</summary>
    Reserved,

    /// <summary>A required field is missing or malformed (HTTP 400).</summary>
    Invalid,

    /// <summary>The target event does not exist (HTTP 404).</summary>
    EventNotFound,

    /// <summary>The event seat does not exist in the event (HTTP 404).</summary>
    EventSeatNotFound,

    /// <summary>The seat was already sold (HTTP 409).</summary>
    AlreadySold,

    /// <summary>Another buyer holds the seat lock (HTTP 409).</summary>
    LockedByAnotherBuyer
}

/// <summary>
/// Outcome of <see cref="ReserveSeatHandler"/>. <see cref="Seat"/> is set only for
/// <see cref="ReserveSeatStatus.Reserved"/>; <see cref="Errors"/> only for
/// <see cref="ReserveSeatStatus.Invalid"/>.
/// </summary>
public sealed record ReserveSeatResult(
    ReserveSeatStatus Status,
    ReservedSeat? Seat,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public static ReserveSeatResult Reserved(ReservedSeat seat) =>
        new(ReserveSeatStatus.Reserved, seat, NoErrors);

    public static ReserveSeatResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(ReserveSeatStatus.Invalid, null, errors);

    public static ReserveSeatResult Failed(ReserveSeatStatus status) =>
        new(status, null, NoErrors);
}
