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
        Assert.Equal(1, context.Sales.ConfirmCalls);
        Assert.Single(context.Outbox.Committed);
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
        sold.Sell(Purchase.Start("Ana Lopez", "ana@example.com", DateTime.UtcNow, SessionDuration), DateTime.UtcNow);
        var context = new Context([held, sold]);
        var session = context.SessionHolding(held, sold);

        var result = await context.Handler().HandleAsync(new(session.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmPurchaseStatus.AlreadySold, result.Status);
        Assert.Equal(EventSeatStatus.Available, held.Status);
        Assert.Equal(0, context.Payment.CaptureCalls);
        Assert.Empty(context.Outbox.Committed);
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
        private readonly FakeVenueSeatCatalog _venueCatalog;

        public Context(IReadOnlyList<EventSeat> seats, FakeVenueSeatCatalog? venueCatalog = null)
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

        public ConfirmPurchaseHandler Handler(TimeSpan? captureTimeout = null) =>
            new(Sessions,
                Sales,
                _venueCatalog,
                Locks,
                Cache,
                Outbox,
                Payment,
                new PaymentOptions { CaptureTimeout = captureTimeout ?? TimeSpan.FromSeconds(10) },
                NullLogger<ConfirmPurchaseHandler>.Instance);

        /// <summary>A live session holding the given seats (locked for it) with a payment token.</summary>
        public Purchase SessionHolding(params EventSeat[] seats) => SessionHolding(DateTime.UtcNow, seats);

        public Purchase SessionHolding(DateTime startedAtUtc, params EventSeat[] seats)
        {
            var session = SessionWithoutToken(startedAtUtc, seats);
            session.SetPaymentToken(Token);
            return session;
        }

        public Purchase SessionWithoutToken(params EventSeat[] seats) => SessionWithoutToken(DateTime.UtcNow, seats);

        private Purchase SessionWithoutToken(DateTime startedAtUtc, EventSeat[] seats)
        {
            var session = Purchase.Start("Juan Perez", "juan.perez@example.com", startedAtUtc, SessionDuration);
            if (seats.Length > 0)
            {
                session.Hold(EventId, seats.Select(seat => seat.Id), 3, startedAtUtc);
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
