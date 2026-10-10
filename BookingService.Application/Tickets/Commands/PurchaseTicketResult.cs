using BookingService.Domain.Tickets;

namespace BookingService.Application.Tickets.Commands;

public enum PurchaseTicketStatus
{
    /// <summary>The seat was sold by this request (HTTP 201).</summary>
    Purchased,

    /// <summary>The idempotency key was already used for this seat; the original sale is returned (HTTP 200).</summary>
    Replayed,

    /// <summary>A required field is missing or malformed (HTTP 400).</summary>
    Invalid,

    /// <summary>The target event does not exist (HTTP 404).</summary>
    EventNotFound,

    /// <summary>The seat does not exist in the event (HTTP 404).</summary>
    TicketNotFound,

    /// <summary>The seat was already sold to another request (HTTP 409).</summary>
    AlreadySold,

    /// <summary>The idempotency key was already used to buy a different seat (HTTP 409).</summary>
    IdempotencyKeyConflict,

    /// <summary>The buyer does not hold a current reservation (seat lock) of the seat (HTTP 403).</summary>
    ReservationRequired
}

/// <summary>
/// Outcome of <see cref="PurchaseTicketHandler"/>. <see cref="Ticket"/> is set for
/// <see cref="PurchaseTicketStatus.Purchased"/> and <see cref="PurchaseTicketStatus.Replayed"/>;
/// <see cref="Errors"/> is keyed by JSON field name and only populated for
/// <see cref="PurchaseTicketStatus.Invalid"/>.
/// </summary>
public sealed record PurchaseTicketResult(
    PurchaseTicketStatus Status,
    Ticket? Ticket,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public bool IsSuccess => Status is PurchaseTicketStatus.Purchased or PurchaseTicketStatus.Replayed;

    public static PurchaseTicketResult Purchased(Ticket ticket) =>
        new(PurchaseTicketStatus.Purchased, ticket, NoErrors);

    public static PurchaseTicketResult Replayed(Ticket ticket) =>
        new(PurchaseTicketStatus.Replayed, ticket, NoErrors);

    public static PurchaseTicketResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(PurchaseTicketStatus.Invalid, null, errors);

    public static PurchaseTicketResult Failed(PurchaseTicketStatus status) =>
        new(status, null, NoErrors);
}
