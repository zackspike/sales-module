using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Application.Purchases.ReadModels;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using Microsoft.Extensions.Logging;

namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// "Pagar": sells every seat the purchase holds and captures its payment, idempotently per
/// <c>Idempotency-Key</c>. The session is checked first (ADR-002). A key that already paid this purchase
/// returns the same tickets right away, before the rules of a new payment, which no longer hold once it is
/// paid. Otherwise the session needs enough time left for the capture (decision 15), held seats
/// and a payment token before anything reaches Payment. Then, in one transaction, the seats are sold, one
/// <c>ticketSold</c> per ticket goes to the outbox and Payment's <c>/capture</c> is called; the repository
/// commits only if the capture succeeded (ADR-003), so a failed capture sells nothing. If some held seats are
/// missing or sold, nothing is sold and every one of them is reported with its reason.
/// The "a seat is sold only once" rule lives in <see cref="EventSeat.Sell(PurchaseSession, DateTime)"/>.
/// Once paid, the sold seats leave the session, so it can hold and pay other seats until it expires.
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
        // A zero or negative value would silently turn off the margin of decision 15.
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.CaptureTimeout, TimeSpan.Zero, "Payment:CaptureTimeout");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.CaptureAttempts, "Payment:CaptureAttempts");

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

        // A replay answers 200 with the same tickets even when too little time is left for a new capture or
        // the paid seats already left the session (contract 1.5).
        if (await _sales.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken) is { } paid)
        {
            return paid.PurchaseId == purchase.Id
                ? ConfirmPurchaseResult.Replayed(
                    new ConfirmedPurchase(purchase.Id, await TicketsOfAsync(paid.Sales, cancellationToken)))
                : ConfirmPurchaseResult.Failed(ConfirmPurchaseStatus.IdempotencyKeyConflict);
        }

        // Too little time left for the capture: Payment could void the token, and the seat locks
        // (which expire with the session, decision 17) could run out while the sale is in flight (decision 15).
        if (purchase.ExpiresAtUtc - nowUtc < _options.PaymentMargin)
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

        // The seats are sold because the session holds them: no lock holder is checked here. Their locks were
        // taken with the session's own deadline and expire with it (ADR-001, decision 17), and the session has at
        // least the payment margin left, so while it is alive its locks are too.

        // Read-only data resolved once inside the transaction, before the capture, and reused for the tickets:
        // a second catalog lookup after the commit could fail a purchase that is already paid. The outcome's
        // status never depends on it.
        IReadOnlyDictionary<Guid, string> seatNumbers = new Dictionary<Guid, string>();

        var outcome = await _sales.SellOnceAsync(
            command.IdempotencyKey,
            purchase,
            seat => seat.Sell(purchase, nowUtc),
            async (sales, transactionCancellationToken) =>
            {
                seatNumbers = await SeatNumbersOfAsync(sales, transactionCancellationToken);
                return await RecordAndCaptureAsync(purchase.Id, paymentToken, sales, transactionCancellationToken);
            },
            cancellationToken);

        return outcome.Status switch
        {
            PurchaseSaleStatus.Sold => await PurchasedAsync(purchase, outcome.Sales, seatNumbers),
            PurchaseSaleStatus.Replayed => ConfirmPurchaseResult.Replayed(
                new ConfirmedPurchase(purchase.Id, await TicketsOfAsync(outcome.Sales, cancellationToken))),
            PurchaseSaleStatus.SeatsUnavailable => ConfirmPurchaseResult.SeatsUnavailable(
                SeatsUnavailableStatusOf(outcome.FailedSeats),
                outcome.FailedSeats),
            _ => ConfirmPurchaseResult.Failed(FailureStatusOf(outcome))
        };
    }

    /// <summary>
    /// What runs inside the sale's transaction once the seat numbers are resolved (so a seat with broken venue
    /// data has already rolled back instead of being charged): records one <c>ticketSold</c> per ticket and
    /// captures the payment. The repository commits only if the capture succeeded.
    /// <para>
    /// The capture ignores the request's cancellation: if the fan closes the tab mid-capture, Payment may still
    /// charge, so Booking keeps waiting for its answer instead of rolling back a paid sale (decision 15).
    /// <c>Payment:CaptureTimeout</c>, enforced by the payment client, bounds the wait.
    /// </para>
    /// </summary>
    private async Task<PaymentCaptureResult> RecordAndCaptureAsync(
        Guid purchaseId,
        string paymentToken,
        IReadOnlyList<EventSeatSale> sales,
        CancellationToken cancellationToken)
    {
        await _outbox.AddAsync(sales.Select(sale => TicketSoldOf(purchaseId, sale)).ToList(), cancellationToken);
        return await _payment.CaptureAsync(paymentToken, CancellationToken.None);
    }

    private async Task<ConfirmPurchaseResult> PurchasedAsync(
        PurchaseSession purchase,
        IReadOnlyList<EventSeatSale> sales,
        IReadOnlyDictionary<Guid, string> seatNumbers)
    {
        await AfterPurchaseCommittedAsync(purchase, sales);
        return ConfirmPurchaseResult.Purchased(new ConfirmedPurchase(purchase.Id, TicketsOf(sales, seatNumbers)));
    }

    /// <summary>
    /// One status for the whole request, as the contract asks: <c>SEAT_ALREADY_SOLD</c> (409) if any seat was
    /// sold, <c>SEAT_NOT_FOUND</c> (404) if every failed seat is missing. The per-seat reasons travel in
    /// <see cref="ConfirmPurchaseResult.FailedSeats"/>.
    /// </summary>
    private static ConfirmPurchaseStatus SeatsUnavailableStatusOf(IReadOnlyList<FailedEventSeat> failedSeats) =>
        failedSeats.Any(seat => seat.Reason == SeatSaleFailure.AlreadySold)
            ? ConfirmPurchaseStatus.AlreadySold
            : ConfirmPurchaseStatus.EventSeatNotFound;

    private static ConfirmPurchaseStatus FailureStatusOf(PurchaseSaleOutcome outcome) => outcome.Status switch
    {
        PurchaseSaleStatus.IdempotencyKeyConflict => ConfirmPurchaseStatus.IdempotencyKeyConflict,
        PurchaseSaleStatus.NotCaptured => outcome.CaptureStatus switch
        {
            PaymentCallStatus.NotAuthorized => ConfirmPurchaseStatus.PaymentNotAuthorized,
            PaymentCallStatus.TimedOut => ConfirmPurchaseStatus.PaymentTimeout,
            _ => ConfirmPurchaseStatus.PaymentUnavailable
        },
        _ => throw new InvalidOperationException($"Unexpected purchase sale status {outcome.Status}.")
    };

    private static TicketSoldEvent TicketSoldOf(Guid purchaseId, EventSeatSale sale) => new(
        purchaseId,
        sale.Ticket.TicketCode,
        sale.Seat.EventId,
        sale.Seat.Id,
        sale.Ticket.HolderName,
        sale.Ticket.HolderEmail,
        sale.Ticket.SoldAtUtc);

    private async Task<IReadOnlyList<IssuedTicket>> TicketsOfAsync(
        IReadOnlyList<EventSeatSale> sales,
        CancellationToken cancellationToken) =>
        TicketsOf(sales, await SeatNumbersOfAsync(sales, cancellationToken));

    private static IReadOnlyList<IssuedTicket> TicketsOf(
        IReadOnlyList<EventSeatSale> sales,
        IReadOnlyDictionary<Guid, string> seatNumbers) =>
        sales.Select(sale => IssuedTicket.From(sale, seatNumbers[sale.Seat.VenueSeatId])).ToList();

    /// <summary>
    /// Seat numbers of the sold seats, keyed by venue seat id, in one catalog call.
    /// </summary>
    /// <exception cref="InvalidOperationException">A seat's venue seat is not in the catalog.</exception>
    private async Task<IReadOnlyDictionary<Guid, string>> SeatNumbersOfAsync(
        IReadOnlyList<EventSeatSale> sales,
        CancellationToken cancellationToken)
    {
        var venueSeatIds = sales.Select(sale => sale.Seat.VenueSeatId).Distinct().ToList();
        var seatNumbers = (await _venueSeatCatalog.GetByIdsAsync(venueSeatIds, cancellationToken))
            .ToDictionary(venueSeat => venueSeat.Id, venueSeat => venueSeat.SeatNumber);

        if (sales.FirstOrDefault(sale => !seatNumbers.ContainsKey(sale.Seat.VenueSeatId)) is { } broken)
        {
            throw new InvalidOperationException(
                $"Event seat {broken.Seat.Id} references venue seat {broken.Seat.VenueSeatId}, which is not in the catalog.");
        }

        return seatNumbers;
    }

    /// <summary>
    /// Updates the session and the hot-path stores once the sale is durable. The sold seats leave the session
    /// (which also drops the spent payment token), so it can hold and pay other seats. Each update is attempted
    /// independently and its failure is only logged: the sale is already committed, the locks and the cache
    /// expire on their own, and replays are answered from the sale repository, not from the session.
    /// Runs with <see cref="CancellationToken.None"/>, so a request cancelled after the commit still updates them.
    /// </summary>
    private async Task AfterPurchaseCommittedAsync(PurchaseSession purchase, IReadOnlyList<EventSeatSale> sales)
    {
        foreach (var sale in sales)
        {
            purchase.Release(sale.Seat.Id);
        }

        await BestEffortAsync("purchase session", purchase.Id, () =>
            _sessions.SaveAsync(purchase, CancellationToken.None));

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
}
