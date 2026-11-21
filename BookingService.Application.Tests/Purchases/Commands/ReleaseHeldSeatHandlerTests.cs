using BookingService.Application.Purchases.Commands;
using BookingService.Application.Tests.EventSeats.Fakes;
using BookingService.Application.Tests.Purchases.Fakes;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;

namespace BookingService.Application.Tests.Purchases.Commands;

public class ReleaseHeldSeatHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VenueSeatId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task Held_seat_is_released_and_leaves_the_session()
    {
        var seat = Seat();
        var session = Session(seat.Id);
        var sessions = new FakePurchaseSessionStore(session);
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, session.Id.ToString(), SessionDuration);

        var result = await Handler(sessions, new FakeEventSeatRepository(seat), locks)
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseHeldSeatStatus.Released, result.Status);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
        Assert.Empty(session.HeldEventSeatIds);
        Assert.Equal(1, sessions.SaveCalls);
    }

    [Fact]
    public async Task Releasing_a_seat_the_purchase_does_not_hold_succeeds()
    {
        var seat = Seat();
        var session = Session();

        var result = await Handler(new FakePurchaseSessionStore(session), new FakeEventSeatRepository(seat), new FakeEventSeatLockStore())
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseHeldSeatStatus.Released, result.Status);
    }

    [Fact]
    public async Task Seat_held_by_another_purchase_is_not_released_and_leaves_the_session()
    {
        var seat = Seat();
        var session = Session(seat.Id);
        var sessions = new FakePurchaseSessionStore(session);
        var otherHolder = Guid.NewGuid().ToString();
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, otherHolder, SessionDuration);

        var result = await Handler(sessions, new FakeEventSeatRepository(seat), locks)
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseHeldSeatStatus.HeldByAnotherPurchase, result.Status);
        Assert.Equal(otherHolder, locks.GetHolderIdentifier(seat.Id));
        Assert.Empty(session.HeldEventSeatIds);
        Assert.Equal(1, sessions.SaveCalls);
    }

    [Fact]
    public async Task Sold_seat_is_rejected()
    {
        var seat = Seat();
        var session = Session();
        seat.Sell(session, DateTime.UtcNow);

        var result = await Handler(new FakePurchaseSessionStore(session), new FakeEventSeatRepository(seat), new FakeEventSeatLockStore())
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseHeldSeatStatus.AlreadySold, result.Status);
    }

    [Fact]
    public async Task Expired_session_is_rejected_without_querying_postgres_and_frees_its_seats()
    {
        var seat = Seat();
        var session = ExpiredSession(seat.Id);
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, session.Id.ToString(), SessionDuration);
        var repository = new FakeEventSeatRepository(seat);
        var catalog = new FakeEventCatalog(EventId);

        var result = await Handler(new FakePurchaseSessionStore(session), repository, locks, catalog)
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseHeldSeatStatus.SessionExpired, result.Status);
        Assert.Empty(catalog.RequestedIds);
        Assert.Empty(repository.RequestedIds);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public async Task Unknown_session_is_rejected_without_querying_postgres()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat);
        var catalog = new FakeEventCatalog(EventId);

        var result = await Handler(new FakePurchaseSessionStore(), repository, new FakeEventSeatLockStore(), catalog)
            .HandleAsync(new ReleaseHeldSeatCommand(Guid.NewGuid(), EventId, seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseHeldSeatStatus.SessionExpired, result.Status);
        Assert.Empty(catalog.RequestedIds);
        Assert.Empty(repository.RequestedIds);
    }

    [Fact]
    public async Task Unknown_event_returns_not_found()
    {
        var seat = Seat();
        var session = Session(seat.Id);

        var result = await Handler(
                new FakePurchaseSessionStore(session),
                new FakeEventSeatRepository(seat),
                new FakeEventSeatLockStore(),
                new FakeEventCatalog())
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseHeldSeatStatus.EventNotFound, result.Status);
        Assert.Equal([seat.Id], session.HeldEventSeatIds);
    }

    [Fact]
    public async Task Unknown_seat_returns_not_found()
    {
        var session = Session();

        var result = await Handler(new FakePurchaseSessionStore(session), new FakeEventSeatRepository(Seat()), new FakeEventSeatLockStore())
            .HandleAsync(Command(session, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ReleaseHeldSeatStatus.EventSeatNotFound, result.Status);
    }

    private static ReleaseHeldSeatCommand Command(Purchase session, Guid eventSeatId) =>
        new(session.Id, EventId, eventSeatId);

    private static ReleaseHeldSeatHandler Handler(
        FakePurchaseSessionStore sessions,
        FakeEventSeatRepository repository,
        FakeEventSeatLockStore locks,
        FakeEventCatalog? catalog = null) =>
        new(sessions, catalog ?? new FakeEventCatalog(EventId), repository, locks);

    private static Purchase Session(params Guid[] heldEventSeatIds)
    {
        var nowUtc = DateTime.UtcNow;
        var session = Purchase.Start("Juan Perez", "juan.perez@example.com", nowUtc, SessionDuration);
        if (heldEventSeatIds.Length > 0)
        {
            session.Hold(EventId, heldEventSeatIds, heldEventSeatIds.Length, nowUtc);
        }

        return session;
    }

    private static Purchase ExpiredSession(Guid heldEventSeatId)
    {
        var startedAtUtc = DateTime.UtcNow - SessionDuration - TimeSpan.FromMinutes(1);
        var session = Purchase.Start("Juan Perez", "juan.perez@example.com", startedAtUtc, SessionDuration);
        session.Hold(EventId, [heldEventSeatId], 3, startedAtUtc);
        return session;
    }

    private static EventSeat Seat() => EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
}
