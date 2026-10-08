using BookingService.Domain.Tickets;

namespace BookingService.Application.Tickets.Commands;

public enum ReserveSeatStatus
{
    /// <summary>The seat is locked for the buyer, newly or renewed (HTTP 200).</summary>
    Reserved,

    /// <summary>A required field is missing or malformed (HTTP 400).</summary>
    Invalid,

    /// <summary>The target event does not exist (HTTP 404).</summary>
    EventNotFound,

    /// <summary>The seat does not exist in the event (HTTP 404).</summary>
    TicketNotFound,

    /// <summary>The seat was already sold (HTTP 409).</summary>
    AlreadySold,

    /// <summary>Another buyer holds the seat lock (HTTP 409).</summary>
    LockedByAnotherBuyer
}

/// <summary>
/// Outcome of <see cref="ReserveSeatHandler"/>. <see cref="Ticket"/> and <see cref="ExpiresAtUtc"/>
/// are set only for <see cref="ReserveSeatStatus.Reserved"/>; <see cref="Errors"/> only for
/// <see cref="ReserveSeatStatus.Invalid"/>.
/// </summary>
public sealed record ReserveSeatResult(
    ReserveSeatStatus Status,
    Ticket? Ticket,
    DateTime? ExpiresAtUtc,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public static ReserveSeatResult Reserved(Ticket ticket, DateTime expiresAtUtc) =>
        new(ReserveSeatStatus.Reserved, ticket, expiresAtUtc, NoErrors);

    public static ReserveSeatResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(ReserveSeatStatus.Invalid, null, null, errors);

    public static ReserveSeatResult Failed(ReserveSeatStatus status) =>
        new(status, null, null, NoErrors);
}
