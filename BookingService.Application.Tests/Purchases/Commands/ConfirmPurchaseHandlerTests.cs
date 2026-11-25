using BookingService.Application.Abstractions;
using BookingService.Application.Purchases;
using BookingService.Application.Purchases.Commands;
using BookingService.Application.Tests.EventSeats.Fakes;
using BookingService.Application.Tests.Purchases.Fakes;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using BookingService.Domain.Venues;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Application.Tests.Purchases.Commands;

public class ConfirmPurchaseHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(10);
    private const string Token = "pay_tok_1";

    [Fact]
    public async Task Paying_sells_every_held_seat_records_ticket_sold_and_captures_the_payment()
    {
        var first = Seat();
        var second = Seat();
        var context = new Context([first, second]);
        var session = context.SessionHolding(first, second);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.Purchased, result.Status);
        Assert.Equal(session.Id, result.Purchase!.PurchaseId);
        Assert.Equal(["A-1", "A-2"], result.Purchase.Tickets.Select(ticket => ticket.SeatNumber));
        Assert.All(result.Purchase.Tickets, ticket =>
        {
            Assert.Equal("Juan Perez", ticket.HolderName);
            Assert.Equal("juan.perez@example.com", ticket.HolderEmail);
            Assert.Equal(EventId, ticket.EventId);
        });

        Assert.Equal(EventSeatStatus.Sold, first.Status);
        Assert.Equal(EventSeatStatus.Sold, second.Status);

        Assert.Equal(1, context.Payment.CaptureCalls);
        Assert.Equal(Token, context.Payment.LastCapturedToken);

        Assert.Equal(
            result.Purchase.Tickets.Select(ticket => ticket.TicketCode),
            context.Outbox.Committed.Select(message => message.TicketCode));
        Assert.All(context.Outbox.Committed, message => Assert.Equal(session.Id, message.PurchaseId));
        Assert.Equal([first.Id, second.Id], context.Outbox.Committed.Select(message => message.EventSeatId));

        // The sold seats are no longer held, and the event's cached seats are dropped.
        Assert.Empty(context.Locks.Holders);
        Assert.Equal(1, context.Cache.InvalidateCalls);

        // The sold seats and the spent token leave the session, so it can hold other seats.
        var stored = context.Sessions.Sessions[session.Id];
        Assert.Empty(stored.HeldEventSeatIds);
        Assert.Null(stored.PaymentToken);
    }

    [Fact]
    public async Task Client_disconnecting_during_the_capture_does_not_undo_a_paid_sale()
    {
        // The fan closes the tab while /capture is in flight: Payment may already have charged, so the capture
        // must not be cancelled and the sale must commit.
        var seat = Seat();
        var context = new Context([seat]);
        var session = context.SessionHolding(seat);
        using var request = new CancellationTokenSource();
        context.Payment.DuringCapture = request.Cancel;

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), request.Token);

        Assert.Equal(ConfirmPurchaseStatus.Purchased, result.Status);
        Assert.False(context.Payment.LastCaptureCancellationToken.CanBeCanceled);
        Assert.Equal(EventSeatStatus.Sold, seat.Status);
        Assert.Single(context.Outbox.Committed);
    }

    [Fact]
    public async Task Paid_purchase_returns_its_tickets_even_if_the_catalog_fails_after_the_commit()
    {
        // The seat numbers are resolved once inside the transaction and reused: a second lookup after the
        // commit could turn a paid purchase into an error.
        var seat = Seat();
        var catalog = new CountingVenueSeatCatalog(new VenueSeat(seat.VenueSeatId, ZoneId, "A-1")) { FailAfterCalls = 1 };
        var context = new Context([seat], catalog);
        var session = context.SessionHolding(seat);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.Purchased, result.Status);
        Assert.Equal("A-1", result.Purchase!.Tickets.Single().SeatNumber);
        Assert.Equal(1, catalog.GetByIdsCalls);
    }

    [Theory]
    [InlineData(PaymentCallStatus.NotAuthorized, ConfirmPurchaseStatus.PaymentNotAuthorized)]
    [InlineData(PaymentCallStatus.Unavailable, ConfirmPurchaseStatus.PaymentUnavailable)]
    [InlineData(PaymentCallStatus.TimedOut, ConfirmPurchaseStatus.PaymentTimeout)]
    public async Task Failed_capture_sells_nothing_and_records_no_ticket_sold(
        PaymentCallStatus captureStatus,
        ConfirmPurchaseStatus expected)
    {
        var seat = Seat();
        var context = new Context([seat]);
        context.Payment.CaptureResult = new(captureStatus);
        var session = context.SessionHolding(seat);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Purchase);
        Assert.Equal(1, context.Payment.CaptureCalls);
        Assert.Equal(EventSeatStatus.Available, seat.Status);
        Assert.Empty(context.Outbox.Committed);

        // The session lives on, so the fan can retry "Pagar" with the seats still held.
        Assert.Equal(session.Id.ToString(), context.Locks.GetHolderIdentifier(seat.Id));
        Assert.Equal(0, context.Cache.InvalidateCalls);
    }

    [Fact]
    public async Task Retry_after_a_failed_capture_with_the_same_key_can_still_pay()
    {
        var seat = Seat();
        var context = new Context([seat]);
        context.Payment.CaptureResult = new(PaymentCallStatus.TimedOut);
        var session = context.SessionHolding(seat);
        var key = Guid.NewGuid();
        var handler = context.Handler();

        await handler.HandleAsync(new(session.Id, key), CancellationToken.None);
        context.Payment.CaptureResult = new(PaymentCallStatus.Succeeded);
        var result = await handler.HandleAsync(new(session.Id, key), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.Purchased, result.Status);
        Assert.Equal(EventSeatStatus.Sold, seat.Status);
        Assert.Single(context.Outbox.Committed);
    }

    [Fact]
    public async Task Replay_with_the_same_key_returns_the_same_tickets_without_selling_or_charging_again()
    {
        var seat = Seat();
        var context = new Context([seat]);
        var session = context.SessionHolding(seat);
        var key = Guid.NewGuid();
        var handler = context.Handler();

        var first = await handler.HandleAsync(new(session.Id, key), CancellationToken.None);
        var replay = await handler.HandleAsync(new(session.Id, key), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.Purchased, first.Status);
        Assert.Equal(ConfirmPurchaseStatus.Replayed, replay.Status);
        Assert.Equal(
            first.Purchase!.Tickets.Select(ticket => ticket.TicketCode),
            replay.Purchase!.Tickets.Select(ticket => ticket.TicketCode));
        Assert.Equal("A-1", replay.Purchase.Tickets.Single().SeatNumber);

        Assert.Equal(1, context.Payment.CaptureCalls);
        Assert.Equal(1, context.Sales.CaptureCallbacks);
        Assert.Equal(1, context.Sales.SellCalls);
        Assert.Single(context.Outbox.Committed);
    }

    [Fact]
    public async Task Replay_with_less_time_left_than_the_capture_timeout_still_returns_the_tickets()
    {
        // The fan paid, the response was lost and the retry arrives when the session is almost over: the
        // contract (1.5) answers a replay with 200, so the margin of decision 15 must not apply to it.
        var seat = Seat();
        var context = new Context([seat]);
        var session = context.SessionHolding(seat);
        var key = Guid.NewGuid();
        var first = await context.Handler().HandleAsync(new(session.Id, key), CancellationToken.None);

        var replay = await context.Handler(captureTimeout: SessionDuration + TimeSpan.FromMinutes(1))
            .HandleAsync(new(session.Id, key), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.Replayed, replay.Status);
        Assert.Equal(
            first.Purchase!.Tickets.Select(ticket => ticket.TicketCode),
            replay.Purchase!.Tickets.Select(ticket => ticket.TicketCode));
        Assert.Equal(1, context.Payment.CaptureCalls);
    }

    [Fact]
    public async Task Session_can_hold_and_pay_other_seats_after_paying()
    {
        var paid = Seat();
        var next = Seat();
        var context = new Context([paid, next]);
        var session = context.SessionHolding(paid);
        var handler = context.Handler();
        await handler.HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        var stored = context.Sessions.Sessions[session.Id];
        stored.Hold(EventId, next.Id, 3, DateTime.UtcNow);
        context.Locks.TryAcquire(next.Id, session.Id.ToString(), SessionDuration);
        stored.SetPaymentToken("pay_tok_2");

        var result = await handler.HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.Purchased, result.Status);
        Assert.Equal(next.Id, result.Purchase!.Tickets.Single().EventSeatId);
        Assert.Equal("pay_tok_2", context.Payment.LastCapturedToken);
        Assert.Equal(EventSeatStatus.Sold, next.Status);
    }

    [Fact]
    public void Capture_timeout_and_attempts_must_be_positive()
    {
        var context = new Context([]);

        Assert.Throws<ArgumentOutOfRangeException>(() => context.Handler(captureTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.Handler(captureAttempts: 0));
    }

    [Fact]
    public async Task Key_already_used_by_another_purchase_is_a_conflict()
    {
        var paid = Seat();
        var other = Seat();
        var context = new Context([paid, other]);
        var firstSession = context.SessionHolding(paid);
        var secondSession = context.SessionHolding(other);
        var key = Guid.NewGuid();
        var handler = context.Handler();
        await handler.HandleAsync(new(firstSession.Id, key), CancellationToken.None);

        var result = await handler.HandleAsync(new(secondSession.Id, key), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.IdempotencyKeyConflict, result.Status);
        Assert.Equal(EventSeatStatus.Available, other.Status);
        Assert.Equal(1, context.Payment.CaptureCalls);
    }

    [Fact]
    public async Task Purchase_without_seats_is_rejected_without_calling_payment()
    {
        var context = new Context([]);
        var session = context.SessionHolding();
        session.SetPaymentToken(Token);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.NoSeatsHeld, result.Status);
        Assert.Equal(0, context.Sales.SellCalls);
        Assert.Equal(0, context.Payment.CaptureCalls);
    }

    [Fact]
    public async Task Purchase_without_a_payment_token_is_not_authorized_and_payment_is_not_called()
    {
        // Payment was never started, or the held seats changed after it and cleared the token (ADR-005).
        var seat = Seat();
        var context = new Context([seat]);
        var session = context.SessionWithoutToken(seat);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.PaymentNotAuthorized, result.Status);
        Assert.Equal(0, context.Sales.SellCalls);
        Assert.Equal(0, context.Payment.CaptureCalls);
        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }

    [Fact]
    public async Task Session_with_less_time_left_than_the_capture_timeout_is_expired_without_calling_payment()
    {
        var seat = Seat();
        var context = new Context([seat]);
        var startedAtUtc = DateTime.UtcNow - SessionDuration + TimeSpan.FromSeconds(5);
        var session = context.SessionHolding(startedAtUtc, seat);

        var result = await context.Handler(captureTimeout: TimeSpan.FromSeconds(10))
            .HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.SessionExpired, result.Status);
        Assert.Equal(0, context.Sales.SellCalls);
        Assert.Equal(0, context.Payment.CaptureCalls);
        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }

    [Fact]
    public async Task Margin_configured_for_more_capture_attempts_expires_a_session_with_time_for_only_one()
    {
        // The margin is one capture timeout by default (contract, decision 15). If CaptureAttempts is raised to
        // cover the retry of decision 11, 15 s left no longer covers two 10 s captures.
        var seat = Seat();
        var context = new Context([seat]);
        var startedAtUtc = DateTime.UtcNow - SessionDuration + TimeSpan.FromSeconds(15);
        var session = context.SessionHolding(startedAtUtc, seat);

        var result = await context.Handler(captureTimeout: TimeSpan.FromSeconds(10), captureAttempts: 2)
            .HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.SessionExpired, result.Status);
        Assert.Equal(0, context.Sales.SellCalls);
        Assert.Equal(0, context.Payment.CaptureCalls);
    }

    [Fact]
    public async Task Expired_session_frees_its_seats_without_calling_payment()
    {
        var seat = Seat();
        var context = new Context([seat]);
        var startedAtUtc = DateTime.UtcNow - SessionDuration - TimeSpan.FromMinutes(1);
        var session = context.SessionHolding(startedAtUtc, seat);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.SessionExpired, result.Status);
        Assert.Equal(0, context.Sales.SellCalls);
        Assert.Equal(0, context.Payment.CaptureCalls);
        Assert.Empty(context.Locks.Holders);
    }

    [Fact]
    public async Task Unknown_session_is_expired_without_calling_payment()
    {
        var context = new Context([]);

        var result = await context.Handler().HandleAsync(new(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.SessionExpired, result.Status);
        Assert.Equal(0, context.Payment.CaptureCalls);
    }

    [Fact]
    public async Task Empty_idempotency_key_is_invalid()
    {
        var seat = Seat();
        var context = new Context([seat]);
        var session = context.SessionHolding(seat);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.Empty), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.Invalid, result.Status);
        Assert.Contains("idempotencyKey", result.Errors.Keys);
        Assert.Equal(0, context.Sales.SellCalls);
    }

    [Fact]
    public async Task Seat_already_sold_sells_nothing_and_does_not_charge()
    {
        var held = Seat();
        var sold = Seat();
        var otherBuyer = PurchaseSession.Start(sold.EventId, "Ana Lopez", "ana@example.com", DateTime.UtcNow, SessionDuration);
        otherBuyer.Hold(sold.EventId, sold.Id, 1, DateTime.UtcNow);
        sold.Sell(otherBuyer, DateTime.UtcNow);
        var context = new Context([held, sold]);
        var session = context.SessionHolding(held, sold);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.AlreadySold, result.Status);
        Assert.Equal([new FailedEventSeat(sold.Id, SeatSaleFailure.AlreadySold)], result.FailedSeats);
        Assert.Equal(EventSeatStatus.Available, held.Status);
        Assert.Equal(0, context.Payment.CaptureCalls);
        Assert.Empty(context.Outbox.Committed);
    }

    [Fact]
    public async Task Every_failed_seat_is_reported_with_its_reason()
    {
        var held = Seat();
        var sold = Seat();
        var missing = Seat();
        var otherBuyer = PurchaseSession.Start(sold.EventId, "Ana Lopez", "ana@example.com", DateTime.UtcNow, SessionDuration);
        otherBuyer.Hold(sold.EventId, sold.Id, 1, DateTime.UtcNow);
        sold.Sell(otherBuyer, DateTime.UtcNow);

        // `missing` is held by the session but no longer exists in the event.
        var context = new Context([held, sold]);
        var session = context.SessionHolding(held, sold, missing);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.AlreadySold, result.Status);
        Assert.Equal(
            [
                new FailedEventSeat(sold.Id, SeatSaleFailure.AlreadySold),
                new FailedEventSeat(missing.Id, SeatSaleFailure.NotFound)
            ],
            result.FailedSeats);
        Assert.Equal(EventSeatStatus.Available, held.Status);
        Assert.Equal(0, context.Payment.CaptureCalls);
    }

    [Fact]
    public async Task Only_missing_seats_answer_seat_not_found()
    {
        var missing = Seat();
        var context = new Context([]);
        var session = context.SessionHolding(missing);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.EventSeatNotFound, result.Status);
        Assert.Equal([new FailedEventSeat(missing.Id, SeatSaleFailure.NotFound)], result.FailedSeats);
        Assert.Equal(0, context.Payment.CaptureCalls);
    }

    [Fact]
    public async Task Seat_with_broken_venue_data_rolls_back_before_the_capture()
    {
        var seat = Seat();
        var context = new Context([seat], venueCatalog: new FakeVenueSeatCatalog());
        var session = context.SessionHolding(seat);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(EventSeatStatus.Available, seat.Status);
        Assert.Equal(0, context.Payment.CaptureCalls);
        Assert.Empty(context.Outbox.Committed);
    }

    private static EventSeat Seat() => EventSeat.CreateAvailable(EventId, Guid.NewGuid(), ZoneId);

    private sealed class Context
    {
        private readonly IVenueSeatCatalog _venueCatalog;

        public Context(IReadOnlyList<EventSeat> seats, IVenueSeatCatalog? venueCatalog = null)
        {
            // Seat numbers follow the order the seats were given: the first one is A-1.
            _venueCatalog = venueCatalog ?? new FakeVenueSeatCatalog(
                seats.Select((seat, index) => new VenueSeat(seat.VenueSeatId, ZoneId, $"A-{index + 1}")).ToArray());
            Sales = new FakePurchaseSaleRepository(Outbox, seats.ToArray());
        }

        public FakePurchaseSessionStore Sessions { get; } = new();

        public FakeTicketSoldOutbox Outbox { get; } = new();

        public FakePurchaseSaleRepository Sales { get; }

        public FakeEventSeatLockStore Locks { get; } = new();

        public FakeEventSeatsCache Cache { get; } = new();

        public FakePaymentGateway Payment { get; } = new();

        public ConfirmPurchaseHandler Handler(TimeSpan? captureTimeout = null, int? captureAttempts = null) =>
            new(Sessions,
                Sales,
                _venueCatalog,
                Locks,
                Cache,
                Outbox,
                Payment,
                new PaymentOptions
                {
                    CaptureTimeout = captureTimeout ?? TimeSpan.FromSeconds(10),
                    CaptureAttempts = captureAttempts ?? new PaymentOptions().CaptureAttempts
                },
                NullLogger<ConfirmPurchaseHandler>.Instance);

        /// <summary>A live session holding the given seats (locked for it) with a payment token.</summary>
        public PurchaseSession SessionHolding(params EventSeat[] seats) => SessionHolding(DateTime.UtcNow, seats);

        public PurchaseSession SessionHolding(DateTime startedAtUtc, params EventSeat[] seats)
        {
            var session = SessionWithoutToken(startedAtUtc, seats);
            session.SetPaymentToken(Token);
            return session;
        }

        public PurchaseSession SessionWithoutToken(params EventSeat[] seats) => SessionWithoutToken(DateTime.UtcNow, seats);

        private PurchaseSession SessionWithoutToken(DateTime startedAtUtc, EventSeat[] seats)
        {
            var session = PurchaseSession.Start(EventId, "Juan Perez", "juan.perez@example.com", startedAtUtc, SessionDuration);
            foreach (var seat in seats)
            {
                session.Hold(EventId, seat.Id, 3, startedAtUtc);
            }

            foreach (var seat in seats)
            {
                Locks.TryAcquire(seat.Id, session.Id.ToString(), SessionDuration);
            }

            Sessions.SaveAsync(session, CancellationToken.None).GetAwaiter().GetResult();
            return session;
        }
    }
}
