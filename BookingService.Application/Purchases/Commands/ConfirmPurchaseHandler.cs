using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Application.Purchases.ReadModels;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using Microsoft.Extensions.Logging;

namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// "Pagar": sells every seat the purchase holds and captures its payment, idempotently per
/// <c>Idempotency-Key</c>. The session is checked first (ADR-002), with enough time left for the capture
/// (decision 15), and nothing reaches Payment without a payment token. Then, in one transaction, the seats
/// are sold, one <c>ticketSold</c> per ticket goes to the outbox and Payment's <c>/capture</c> is called; the
/// transaction commits only if Payment answers 200 (ADR-003), so a failed capture sells nothing.
/// The "a seat is sold only once" rule lives in <see cref="EventSeat.Sell(PurchaseSession, DateTime)"/>.
/// </summary>
public sealed class ConfirmPurchaseHandler
{
    private readonly IPurchaseSessionStore _sessions;
    private readonly IPurchaseSaleRepository _sales;
    private readonly IVenueSeatCatalog _venueSeatCatalog;
    private readonly IEventSeatLockStore _eventSeatLocks;
    private readonly IEventSeatsCache _eventSeatsCache;
    private readonly ITicketSoldOutbox _outbox;
    private readonly IPaymentGateway _payment;
    private readonly PaymentOptions _options;
    private readonly ILogger<ConfirmPurchaseHandler> _logger;

    public ConfirmPurchaseHandler(
        IPurchaseSessionStore sessions,
        IPurchaseSaleRepository sales,
        IVenueSeatCatalog venueSeatCatalog,
        IEventSeatLockStore eventSeatLocks,
        IEventSeatsCache eventSeatsCache,
        ITicketSoldOutbox outbox,
        IPaymentGateway payment,
        PaymentOptions options,
        ILogger<ConfirmPurchaseHandler> logger)
    {
        _sessions = sessions;
        _sales = sales;
        _venueSeatCatalog = venueSeatCatalog;
        _eventSeatLocks = eventSeatLocks;
        _eventSeatsCache = eventSeatsCache;
        _outbox = outbox;
        _payment = payment;
        _options = options;
        _logger = logger;
    }

    public async Task<ConfirmPurchaseResult> HandleAsync(
        ConfirmPurchaseCommand command,
        CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey == Guid.Empty)
        {
            return ConfirmPurchaseResult.Invalid(new Dictionary<string, string[]>
            {
                ["idempotencyKey"] = ["idempotencyKey must be a non-empty UUID."]
            });
        }

        var nowUtc = DateTime.UtcNow;

        var purchase = await _sessions.GetAsync(command.PurchaseId, cancellationToken);
        if (purchase is null)
        {
            return ConfirmPurchaseResult.Failed(ConfirmPurchaseStatus.SessionExpired);
        }

        if (purchase.IsExpired(nowUtc))
        {
            await PurchaseSessionExpiration.ReleaseHeldSeatsAsync(purchase, _eventSeatLocks, cancellationToken);
            return ConfirmPurchaseResult.Failed(ConfirmPurchaseStatus.SessionExpired);
        }

        // Too little time left for the capture: Payment could void the token mid-capture (decision 15).
        if (purchase.ExpiresAtUtc - nowUtc < _options.CaptureTimeout)
        {
            return ConfirmPurchaseResult.Failed(ConfirmPurchaseStatus.SessionExpired);
        }

        if (purchase.HeldEventSeatIds.Count == 0)
        {
            return ConfirmPurchaseResult.Failed(ConfirmPurchaseStatus.NoSeatsHeld);
        }

        // No token: payment was never started, or the seats changed after it (ADR-005).
        if (purchase.PaymentToken is not { } paymentToken)
        {
            return ConfirmPurchaseResult.Failed(ConfirmPurchaseStatus.PaymentNotAuthorized);
        }

        PaymentCallStatus? captureStatus = null;
        IReadOnlyList<IssuedTicket> tickets = [];

        var outcome = await _sales.SellOnceAsync(
            command.IdempotencyKey,
            purchase,
            seat => seat.Sell(purchase, nowUtc),
            async (sales, transactionCancellationToken) =>
            {
                // Resolved before the capture, so a seat with broken venue data rolls back instead of
                // being charged.
                tickets = await TicketsOfAsync(sales, transactionCancellationToken);
                await _outbox.AddAsync(
                    sales.Select(sale => TicketSoldOf(purchase.Id, sale)).ToList(),
                    transactionCancellationToken);

                var capture = await _payment.CaptureAsync(paymentToken, transactionCancellationToken);
                captureStatus = capture.Status;
                return capture.Status == PaymentCallStatus.Succeeded;
            },
            cancellationToken);

        switch (outcome.Status)
        {
            case PurchaseSaleStatus.Sold:
                await AfterPurchaseCommittedAsync(purchase, outcome.Sales);
                return ConfirmPurchaseResult.Purchased(new ConfirmedPurchase(purchase.Id, tickets));
            case PurchaseSaleStatus.Replayed:
                return ConfirmPurchaseResult.Replayed(
                    new ConfirmedPurchase(purchase.Id, await TicketsOfAsync(outcome.Sales, cancellationToken)));
            case PurchaseSaleStatus.IdempotencyKeyConflict:
                return ConfirmPurchaseResult.Failed(ConfirmPurchaseStatus.IdempotencyKeyConflict);
            case PurchaseSaleStatus.EventSeatNotFound:
                return ConfirmPurchaseResult.Failed(ConfirmPurchaseStatus.EventSeatNotFound);
            case PurchaseSaleStatus.AlreadySold:
                return ConfirmPurchaseResult.Failed(ConfirmPurchaseStatus.AlreadySold);
            default:
                return ConfirmPurchaseResult.Failed(captureStatus switch
                {
                    PaymentCallStatus.NotAuthorized => ConfirmPurchaseStatus.PaymentNotAuthorized,
                    PaymentCallStatus.TimedOut => ConfirmPurchaseStatus.PaymentTimeout,
                    _ => ConfirmPurchaseStatus.PaymentUnavailable
                });
        }
    }

    private static TicketSold TicketSoldOf(Guid purchaseId, EventSeatSale sale) => new(
        purchaseId,
        sale.Ticket.TicketCode,
        sale.Seat.EventId,
        sale.Seat.Id,
        sale.Ticket.HolderName,
        sale.Ticket.HolderEmail,
        sale.Ticket.SoldAtUtc);

    private async Task<IReadOnlyList<IssuedTicket>> TicketsOfAsync(
        IReadOnlyList<EventSeatSale> sales,
        CancellationToken cancellationToken)
    {
        var tickets = new List<IssuedTicket>(sales.Count);
        foreach (var sale in sales)
        {
            tickets.Add(IssuedTicket.From(sale, await SeatNumberOfAsync(sale.Seat, cancellationToken)));
        }

        return tickets;
    }

    /// <summary>
    /// Updates the hot-path stores once the sale is durable. Each update is attempted independently and its
    /// failure is only logged: the sale is already committed, and the locks and the cache expire on their own.
    /// Runs with <see cref="CancellationToken.None"/>, so a request cancelled after the commit still updates them.
    /// </summary>
    private async Task AfterPurchaseCommittedAsync(PurchaseSession purchase, IReadOnlyList<EventSeatSale> sales)
    {
        var holder = PurchaseSeatLockHolder.Of(purchase.Id);

        foreach (var sale in sales)
        {
            await BestEffortAsync("seat lock", purchase.Id, () =>
                _eventSeatLocks.ReleaseAsync(sale.Seat.Id, holder, CancellationToken.None));
        }

        foreach (var eventId in sales.Select(sale => sale.Seat.EventId).Distinct())
        {
            await BestEffortAsync("event seats cache", purchase.Id, () =>
                _eventSeatsCache.InvalidateAsync(eventId, CancellationToken.None));
        }
    }

    private async Task BestEffortAsync(string store, Guid purchaseId, Func<Task> update)
    {
        try
        {
            await update();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Purchase {PurchaseId} was paid but the {Store} store could not be updated.",
                purchaseId,
                store);
        }
    }

    private async Task<string> SeatNumberOfAsync(EventSeat eventSeat, CancellationToken cancellationToken) =>
        (await _venueSeatCatalog.GetByIdAsync(eventSeat.VenueSeatId, cancellationToken))?.SeatNumber
            ?? throw new InvalidOperationException(
                $"Event seat {eventSeat.Id} references venue seat {eventSeat.VenueSeatId}, which is not in the catalog.");
}
