namespace BookingService.Application.EventSeats.Commands;

public enum ReleaseEventSeatStatus
{
    /// <summary>The buyer no longer holds the seat: it was released or was not reserved (HTTP 204).</summary>
    Released,

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
/// Outcome of <see cref="ReleaseEventSeatHandler"/>. <see cref="Errors"/> is set only for
/// <see cref="ReleaseEventSeatStatus.Invalid"/>.
/// </summary>
public sealed record ReleaseEventSeatResult(
    ReleaseEventSeatStatus Status,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public static ReleaseEventSeatResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(ReleaseEventSeatStatus.Invalid, errors);

    public static ReleaseEventSeatResult Of(ReleaseEventSeatStatus status) =>
        new(status, NoErrors);
}
