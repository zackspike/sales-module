using BookingService.Application.Abstractions;
using BookingService.Domain.Tickets;
using Microsoft.Extensions.Logging;

namespace BookingService.Application.Tickets.Commands;

/// <summary>
/// Buys a reserved seat (SP-05 / SP-06 / APP-04): validates name, email and event (400/404),
/// answers duplicated requests from the idempotency store, requires the buyer to hold the seat
/// lock taken by <see cref="ReserveSeatHandler"/> (403), and then sells the seat atomically and
/// idempotently through <see cref="ITicketRepository.PurchaseOnce"/>.
/// The "a seat is sold only once" rule lives in <see cref="Domain.Tickets.Ticket.Purchase"/>.
/// </summary>
public sealed class PurchaseTicketHandler
{
    /// <summary>
    /// How long a used idempotency key is answered from the idempotency store.
    /// </summary>
    public static readonly TimeSpan IdempotencyKeyRetention = TimeSpan.FromMinutes(10);

    private readonly TicketPurchaseValidator _validator;
    private readonly ITicketRepository _tickets;
    private readonly ISeatLockStore _seatLocks;
    private readonly IIdempotencyStore _idempotency;
    private readonly IAvailableSeatsCache _availableSeats;
    private readonly ILogger<PurchaseTicketHandler> _logger;

    public PurchaseTicketHandler(
        TicketPurchaseValidator validator,
        ITicketRepository tickets,
        ISeatLockStore seatLocks,
        IIdempotencyStore idempotency,
        IAvailableSeatsCache availableSeats,
        ILogger<PurchaseTicketHandler> logger)
    {
        _validator = validator;
        _tickets = tickets;
        _seatLocks = seatLocks;
        _idempotency = idempotency;
        _availableSeats = availableSeats;
        _logger = logger;
    }

    public PurchaseTicketResult Handle(PurchaseTicketCommand command)
    {
        var validation = _validator.Validate(command);

        if (validation.Status == TicketPurchaseValidationStatus.Invalid)
        {
            return PurchaseTicketResult.Invalid(validation.Errors);
        }

        if (command.IdempotencyKey == Guid.Empty)
        {
            return PurchaseTicketResult.Invalid(new Dictionary<string, string[]>
            {
                ["idempotencyKey"] = ["X-Idempotency-Key is required."]
            });
        }

        if (validation.Status == TicketPurchaseValidationStatus.EventNotFound)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.EventNotFound);
        }

        var fullName = command.FullName!.Trim();
        var email = command.Email!.Trim();

        if (_idempotency.Get(command.IdempotencyKey) is { } remembered)
        {
            return ReplayRememberedPurchase(remembered, command);
        }

        var ticket = _tickets.GetById(command.EventId, command.TicketId);
        if (ticket is null)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.TicketNotFound);
        }

        if (ticket.Status == TicketStatus.Sold)
        {
            return SoldSeatResult(ticket, command);
        }

        var holder = SeatLockHolder.From(email);
        if (_seatLocks.GetHolder(ticket.Id) != holder)
        {
            // A concurrent purchase may have sold the seat and released its lock after the check
            // above; the sale is committed before the release, so a re-read sees it.
            var current = _tickets.GetById(command.EventId, command.TicketId);
            return current?.Status == TicketStatus.Sold
                ? SoldSeatResult(current, command)
                : PurchaseTicketResult.Failed(PurchaseTicketStatus.ReservationRequired);
        }

        TicketPurchaseOutcome outcome;
        try
        {
            outcome = _tickets.PurchaseOnce(
                command.IdempotencyKey,
                command.EventId,
                command.TicketId,
                t => t.Purchase(fullName, email, command.IdempotencyKey, DateTime.UtcNow));
        }
        catch (TicketAlreadySoldException)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.AlreadySold);
        }

        if (outcome.Ticket is null)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.TicketNotFound);
        }

        if (!outcome.Replayed)
        {
            AfterPurchaseCommitted(outcome.Ticket, command, holder);
            return PurchaseTicketResult.Purchased(outcome.Ticket);
        }

        return outcome.Ticket.Id == command.TicketId && outcome.Ticket.EventId == command.EventId
            ? PurchaseTicketResult.Replayed(outcome.Ticket)
            : PurchaseTicketResult.Failed(PurchaseTicketStatus.IdempotencyKeyConflict);
    }

    // Also covers retries whose idempotency entry expired: the sold ticket still knows the key.
    private static PurchaseTicketResult SoldSeatResult(Ticket ticket, PurchaseTicketCommand command) =>
        ticket.IdempotencyKey == command.IdempotencyKey
            ? PurchaseTicketResult.Replayed(ticket)
            : PurchaseTicketResult.Failed(PurchaseTicketStatus.AlreadySold);

    private PurchaseTicketResult ReplayRememberedPurchase(IdempotencyRecord remembered, PurchaseTicketCommand command)
    {
        if (remembered.TicketId != command.TicketId || remembered.EventId != command.EventId)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.IdempotencyKeyConflict);
        }

        var ticket = _tickets.GetById(command.EventId, command.TicketId);
        return ticket is null
            ? PurchaseTicketResult.Failed(PurchaseTicketStatus.TicketNotFound)
            : PurchaseTicketResult.Replayed(ticket);
    }

    /// <summary>
    /// Updates the hot-path stores once the sale is durable. Failures are only logged: the sale is
    /// already committed, the lock and cache expire on their own, and replays fall back to the repository.
    /// </summary>
    private void AfterPurchaseCommitted(Ticket ticket, PurchaseTicketCommand command, string holder)
    {
        try
        {
            _idempotency.Remember(
                command.IdempotencyKey,
                new IdempotencyRecord(command.EventId, ticket.Id),
                IdempotencyKeyRetention);
            _seatLocks.Release(ticket.Id, holder);
            _availableSeats.Invalidate(command.EventId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Ticket {TicketId} was sold but the seat lock/idempotency/cache stores could not be updated.",
                ticket.Id);
        }
    }
}
