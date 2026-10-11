using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.EventSeats;
using Microsoft.Extensions.Logging;

namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Buys a reserved event seat and issues its ticket (SP-05 / SP-06 / APP-04): validates name, email
/// and event (400/404), answers duplicated requests from the idempotency store or the repository,
/// requires the buyer to hold the seat lock taken by <see cref="ReserveSeatHandler"/> (403), and then
/// sells the seat atomically and idempotently through <see cref="IEventSeatRepository.SellOnce"/>.
/// The "a seat is sold only once" rule lives in <see cref="EventSeat.Sell"/>.
/// </summary>
public sealed class PurchaseEventSeatHandler
{
    /// <summary>
    /// How long a used idempotency key is answered from the idempotency store.
    /// </summary>
    public static readonly TimeSpan IdempotencyKeyRetention = TimeSpan.FromMinutes(10);

    private readonly SeatPurchaseValidator _validator;
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IVenueSeatCatalog _venueSeatCatalog;
    private readonly IEventSeatLockStore _eventSeatLocks;
    private readonly IIdempotencyStore _idempotency;
    private readonly IAvailableEventSeatsCache _availableEventSeatsCache;
    private readonly ILogger<PurchaseEventSeatHandler> _logger;

    public PurchaseEventSeatHandler(
        SeatPurchaseValidator validator,
        IEventSeatRepository eventSeatRepository,
        IVenueSeatCatalog venueSeatCatalog,
        IEventSeatLockStore eventSeatLocks,
        IIdempotencyStore idempotency,
        IAvailableEventSeatsCache availableEventSeatsCache,
        ILogger<PurchaseEventSeatHandler> logger)
    {
        _validator = validator;
        _eventSeatRepository = eventSeatRepository;
        _venueSeatCatalog = venueSeatCatalog;
        _eventSeatLocks = eventSeatLocks;
        _idempotency = idempotency;
        _availableEventSeatsCache = availableEventSeatsCache;
        _logger = logger;
    }

    public PurchaseEventSeatResult Handle(PurchaseEventSeatCommand command)
    {
        var validation = _validator.Validate(command);

        if (validation.Status == SeatPurchaseValidationStatus.Invalid)
        {
            return PurchaseEventSeatResult.Invalid(validation.Errors);
        }

        if (command.IdempotencyKey == Guid.Empty)
        {
            return PurchaseEventSeatResult.Invalid(new Dictionary<string, string[]>
            {
                ["idempotencyKey"] = ["X-Idempotency-Key is required."]
            });
        }

        if (validation.Status == SeatPurchaseValidationStatus.EventNotFound)
        {
            return PurchaseEventSeatResult.Failed(PurchaseEventSeatStatus.EventNotFound);
        }

        var fullName = command.FullName!.Trim();
        var email = command.Email!.Trim();

        if (_idempotency.GetIdempotencyRecord(command.IdempotencyKey) is { } remembered)
        {
            return ReplayRememberedPurchase(remembered, command);
        }

        var eventSeat = _eventSeatRepository.GetById(command.EventId, command.EventSeatId);
        if (eventSeat is null)
        {
            return PurchaseEventSeatResult.Failed(PurchaseEventSeatStatus.EventSeatNotFound);
        }

        // Resolved before selling, so a seat with broken venue data is never sold.
        var seatNumber = SeatNumberOf(eventSeat);

        if (eventSeat.Status == EventSeatStatus.Sold)
        {
            return SoldSeatResult(command, seatNumber);
        }

        var holder = SeatLockHolder.From(email);
        if (_eventSeatLocks.GetHolderIdentifier(eventSeat.Id) != holder)
        {
            // A concurrent purchase may have sold the seat and released its lock after the check
            // above; the sale is committed before the release, so a re-read sees it.
            var current = _eventSeatRepository.GetById(command.EventId, command.EventSeatId);
            return current?.Status == EventSeatStatus.Sold
                ? SoldSeatResult(command, seatNumber)
                : PurchaseEventSeatResult.Failed(PurchaseEventSeatStatus.ReservationRequired);
        }

        SeatSaleOutcome outcome;
        try
        {
            outcome = _eventSeatRepository.SellOnce(
                command.IdempotencyKey,
                command.EventId,
                command.EventSeatId,
                seat => seat.Sell(fullName, email, DateTime.UtcNow));
        }
        catch (EventSeatAlreadySoldException)
        {
            return PurchaseEventSeatResult.Failed(PurchaseEventSeatStatus.AlreadySold);
        }

        if (outcome.Sale is null)
        {
            return PurchaseEventSeatResult.Failed(PurchaseEventSeatStatus.EventSeatNotFound);
        }

        if (!outcome.Replayed)
        {
            AfterPurchaseCommitted(outcome.Sale, command, holder);
            return PurchaseEventSeatResult.Purchased(ToIssuedTicket(outcome.Sale, seatNumber));
        }

        return outcome.Sale.Seat.Id == command.EventSeatId && outcome.Sale.Seat.EventId == command.EventId
            ? PurchaseEventSeatResult.Replayed(ToIssuedTicket(outcome.Sale, seatNumber))
            : PurchaseEventSeatResult.Failed(PurchaseEventSeatStatus.IdempotencyKeyConflict);
    }

    // Also covers retries whose idempotency store entry expired: the repository keeps the key of every sale.
    private PurchaseEventSeatResult SoldSeatResult(PurchaseEventSeatCommand command, string seatNumber) =>
        _eventSeatRepository.GetSaleByIdempotencyKey(command.IdempotencyKey, command.EventId, command.EventSeatId) is { } sale
            ? PurchaseEventSeatResult.Replayed(ToIssuedTicket(sale, seatNumber))
            : PurchaseEventSeatResult.Failed(PurchaseEventSeatStatus.AlreadySold);

    private PurchaseEventSeatResult ReplayRememberedPurchase(IdempotencyRecord remembered, PurchaseEventSeatCommand command)
    {
        if (remembered.EventSeatId != command.EventSeatId || remembered.EventId != command.EventId)
        {
            return PurchaseEventSeatResult.Failed(PurchaseEventSeatStatus.IdempotencyKeyConflict);
        }

        var sale = _eventSeatRepository.GetSaleByIdempotencyKey(command.IdempotencyKey, command.EventId, command.EventSeatId);
        return sale is null
            ? PurchaseEventSeatResult.Failed(PurchaseEventSeatStatus.EventSeatNotFound)
            : PurchaseEventSeatResult.Replayed(ToIssuedTicket(sale, SeatNumberOf(sale.Seat)));
    }

    /// <summary>
    /// Updates the hot-path stores once the sale is durable. Failures are only logged: the sale is
    /// already committed, the lock and cache expire on their own, and replays fall back to the repository.
    /// </summary>
    private void AfterPurchaseCommitted(SeatSale sale, PurchaseEventSeatCommand command, string holder)
    {
        try
        {
            _idempotency.Remember(
                command.IdempotencyKey,
                new IdempotencyRecord(command.EventId, sale.Seat.Id),
                IdempotencyKeyRetention);
            _eventSeatLocks.Release(sale.Seat.Id, holder);
            _availableEventSeatsCache.Invalidate(command.EventId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Event seat {EventSeatId} was sold but the seat lock/idempotency/cache stores could not be updated.",
                sale.Seat.Id);
        }
    }

    private string SeatNumberOf(EventSeat eventSeat) =>
        _venueSeatCatalog.GetById(eventSeat.VenueSeatId)?.SeatNumber
            ?? throw new InvalidOperationException(
                $"Event seat {eventSeat.Id} references venue seat {eventSeat.VenueSeatId}, which is not in the catalog.");

    private static IssuedTicket ToIssuedTicket(SeatSale sale, string seatNumber) => new(
        sale.Seat.Id,
        sale.Seat.EventId,
        seatNumber,
        sale.Ticket.HolderName,
        sale.Ticket.HolderEmail,
        sale.Ticket.TicketCode,
        sale.Ticket.SoldAtUtc);
}
