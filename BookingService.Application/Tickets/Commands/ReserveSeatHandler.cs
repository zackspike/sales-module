using BookingService.Application.Abstractions;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tickets.Commands;

/// <summary>
/// First step of the purchase flow: the fan selects a seat and clicks "comprar".
/// Validates name, email and event (400/404), checks the seat is not sold, and locks it for the
/// buyer (identified by email) during <see cref="SeatReservationPolicy.LockDuration"/>.
/// </summary>
public sealed class ReserveSeatHandler
{
    private readonly TicketPurchaseValidator _validator;
    private readonly ITicketRepository _tickets;
    private readonly ISeatLockStore _seatLocks;

    public ReserveSeatHandler(TicketPurchaseValidator validator, ITicketRepository tickets, ISeatLockStore seatLocks)
    {
        _validator = validator;
        _tickets = tickets;
        _seatLocks = seatLocks;
    }

    public ReserveSeatResult Handle(ReserveSeatCommand command)
    {
        var validation = _validator.Validate(command.EventId, command.FullName, command.Email);

        if (validation.Status == TicketPurchaseValidationStatus.Invalid)
        {
            return ReserveSeatResult.Invalid(validation.Errors);
        }

        if (validation.Status == TicketPurchaseValidationStatus.EventNotFound)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.EventNotFound);
        }

        var ticket = _tickets.GetById(command.EventId, command.TicketId);
        if (ticket is null)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.TicketNotFound);
        }

        if (ticket.Status == TicketStatus.Sold)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.AlreadySold);
        }

        var holder = SeatLockHolder.From(command.Email!);
        var expiresAtUtc = DateTime.UtcNow + SeatReservationPolicy.LockDuration;

        if (_seatLocks.TryAcquire(ticket.Id, holder, SeatReservationPolicy.LockDuration) == SeatLockResult.HeldByAnother)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.LockedByAnotherBuyer);
        }

        // The seat may have been sold (and its lock released) between the availability check
        // and the lock: re-check so a sold seat is never left reserved.
        var current = _tickets.GetById(command.EventId, command.TicketId);
        if (current is null || current.Status == TicketStatus.Sold)
        {
            _seatLocks.Release(ticket.Id, holder);
            return ReserveSeatResult.Failed(ReserveSeatStatus.AlreadySold);
        }

        return ReserveSeatResult.Reserved(current, expiresAtUtc);
    }
}

/// <summary>
/// The buyer identity stored in a seat lock: the email, trimmed and case-insensitive.
/// </summary>
internal static class SeatLockHolder
{
    public static string From(string email) => email.Trim().ToLowerInvariant();
}
