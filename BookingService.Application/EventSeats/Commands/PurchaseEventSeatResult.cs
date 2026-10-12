using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.EventSeats.Commands;

public enum PurchaseEventSeatStatus
{
    /// <summary>The seat was sold by this request (HTTP 201).</summary>
    Purchased,

    /// <summary>The idempotency key was already used for this seat; the original sale is returned (HTTP 200).</summary>
    Replayed,

    /// <summary>A required field is missing or malformed (HTTP 400).</summary>
    Invalid,

    /// <summary>The target event does not exist (HTTP 404).</summary>
    EventNotFound,

    /// <summary>The event seat does not exist in the event (HTTP 404).</summary>
    EventSeatNotFound,

    /// <summary>The seat was already sold to another request (HTTP 409).</summary>
    AlreadySold,

    /// <summary>The idempotency key was already used to buy a different seat (HTTP 409).</summary>
    IdempotencyKeyConflict,

    /// <summary>The buyer does not hold a current reservation (seat lock) of the seat (HTTP 403).</summary>
    ReservationRequired
}

/// <summary>
/// Outcome of <see cref="PurchaseEventSeatHandler"/>. <see cref="Ticket"/> is set for
/// <see cref="PurchaseEventSeatStatus.Purchased"/> and <see cref="PurchaseEventSeatStatus.Replayed"/>;
/// <see cref="Errors"/> is keyed by JSON field name and only populated for
/// <see cref="PurchaseEventSeatStatus.Invalid"/>.
/// </summary>
public sealed record PurchaseEventSeatResult(
    PurchaseEventSeatStatus Status,
    IssuedTicket? Ticket,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public static PurchaseEventSeatResult Purchased(IssuedTicket ticket) =>
        new(PurchaseEventSeatStatus.Purchased, ticket, NoErrors);

    public static PurchaseEventSeatResult Replayed(IssuedTicket ticket) =>
        new(PurchaseEventSeatStatus.Replayed, ticket, NoErrors);

    public static PurchaseEventSeatResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(PurchaseEventSeatStatus.Invalid, null, errors);

    public static PurchaseEventSeatResult Failed(PurchaseEventSeatStatus status) =>
        new(status, null, NoErrors);
}
