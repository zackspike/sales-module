using BookingService.Application.Abstractions;
using BookingService.Application.Purchases.Commands;
using BookingService.Application.Tests.EventSeats.Fakes;
using BookingService.Application.Tests.Purchases.Fakes;
using BookingService.Domain.Purchases;

namespace BookingService.Application.Tests.Purchases.Commands;

public class StartPaymentHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task Token_from_init_is_stored_in_the_session_and_returned_with_the_session_deadline()
    {
        var seatIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = SessionHolding(seatIds);
        var sessions = new FakePurchaseSessionStore(session);
        var payment = new FakePaymentGateway { InitResult = new(PaymentCallStatus.Succeeded, "pay_tok_abc") };

        var result = await Handler(sessions, payment).HandleAsync(new(session.Id), CancellationToken.None);

        Assert.Equal(StartPaymentStatus.Started, result.Status);
        Assert.Equal("pay_tok_abc", result.Payment!.Token);
        Assert.Equal(session.ExpiresAtUtc, result.Payment.ExpiresAtUtc);
        Assert.Equal("pay_tok_abc", session.PaymentToken);
        Assert.Equal(1, sessions.SaveCalls);

        Assert.Equal(1, payment.InitCalls);
        Assert.Equal(session.Id, payment.LastInitPurchaseId);
        Assert.Equal(session.ExpiresAtUtc, payment.LastInitExpiresAtUtc);
        Assert.Equal(seatIds, payment.LastInitEventSeatIds);
    }

    [Fact]
    public async Task Payment_is_asked_again_when_the_session_already_has_a_token()
    {
        // Booking cannot tell a VOID token from a valid one: /init decides (decisions 2 and 3).
        var session = SessionHolding(Guid.NewGuid());
        session.SetPaymentToken("pay_tok_voided");
        var payment = new FakePaymentGateway { InitResult = new(PaymentCallStatus.Succeeded, "pay_tok_new") };

        var result = await Handler(new FakePurchaseSessionStore(session), payment)
            .HandleAsync(new(session.Id), CancellationToken.None);

        Assert.Equal(StartPaymentStatus.Started, result.Status);
        Assert.Equal(1, payment.InitCalls);
        Assert.Equal("pay_tok_new", session.PaymentToken);
    }

    [Fact]
    public async Task Unknown_session_is_rejected_without_calling_payment()
    {
        var payment = new FakePaymentGateway();

        var result = await Handler(new FakePurchaseSessionStore(), payment)
            .HandleAsync(new(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(StartPaymentStatus.SessionExpired, result.Status);
        Assert.Equal(0, payment.InitCalls);
    }

    [Fact]
    public async Task Expired_session_frees_its_seats_without_calling_payment()
    {
        var seatId = Guid.NewGuid();
        var startedAtUtc = DateTime.UtcNow - SessionDuration - TimeSpan.FromMinutes(1);
        var session = PurchaseSession.Start(EventId, "Juan Perez", "juan.perez@example.com", startedAtUtc, SessionDuration);
        session.Hold(EventId, seatId, 3, startedAtUtc);
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seatId, session.Id.ToString(), SessionDuration);
        var payment = new FakePaymentGateway();

        var result = await Handler(new FakePurchaseSessionStore(session), payment, locks)
            .HandleAsync(new(session.Id), CancellationToken.None);

        Assert.Equal(StartPaymentStatus.SessionExpired, result.Status);
        Assert.Equal(0, payment.InitCalls);
        Assert.Empty(locks.Holders);
    }

    [Fact]
    public async Task Purchase_without_seats_is_rejected_without_calling_payment()
    {
        var session = SessionHolding();
        var sessions = new FakePurchaseSessionStore(session);
        var payment = new FakePaymentGateway();

        var result = await Handler(sessions, payment).HandleAsync(new(session.Id), CancellationToken.None);

        Assert.Equal(StartPaymentStatus.NoSeatsHeld, result.Status);
        Assert.Equal(0, payment.InitCalls);
        Assert.Equal(0, sessions.SaveCalls);
    }

    [Theory]
    [InlineData(PaymentCallStatus.Unavailable, StartPaymentStatus.PaymentUnavailable)]
    [InlineData(PaymentCallStatus.TimedOut, StartPaymentStatus.PaymentTimeout)]
    public async Task Payment_failure_stores_no_token(PaymentCallStatus initStatus, StartPaymentStatus expected)
    {
        var session = SessionHolding(Guid.NewGuid());
        var sessions = new FakePurchaseSessionStore(session);
        var payment = new FakePaymentGateway { InitResult = new(initStatus, null) };

        var result = await Handler(sessions, payment).HandleAsync(new(session.Id), CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Payment);
        Assert.Null(session.PaymentToken);
        Assert.Equal(0, sessions.SaveCalls);
    }

    [Fact]
    public async Task Success_without_a_token_is_treated_as_payment_unavailable()
    {
        var session = SessionHolding(Guid.NewGuid());
        var payment = new FakePaymentGateway { InitResult = new(PaymentCallStatus.Succeeded, " ") };

        var result = await Handler(new FakePurchaseSessionStore(session), payment)
            .HandleAsync(new(session.Id), CancellationToken.None);

        Assert.Equal(StartPaymentStatus.PaymentUnavailable, result.Status);
        Assert.Null(session.PaymentToken);
    }

    private static StartPaymentHandler Handler(
        FakePurchaseSessionStore sessions,
        FakePaymentGateway payment,
        FakeEventSeatLockStore? locks = null) =>
        new(sessions, locks ?? new FakeEventSeatLockStore(), payment);

    private static PurchaseSession SessionHolding(params Guid[] eventSeatIds)
    {
        var nowUtc = DateTime.UtcNow;
        var session = PurchaseSession.Start(EventId, "Juan Perez", "juan.perez@example.com", nowUtc, SessionDuration);
        foreach (var eventSeatId in eventSeatIds)
        {
            session.Hold(EventId, eventSeatId, 3, nowUtc);
        }

        return session;
    }
}
