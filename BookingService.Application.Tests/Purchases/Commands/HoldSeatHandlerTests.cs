using BookingService.Application.Purchases;
using BookingService.Application.Purchases.Commands;
using BookingService.Application.Tests.EventSeats.Fakes;
using BookingService.Application.Tests.Purchases.Fakes;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using BookingService.Domain.Venues;

namespace BookingService.Application.Tests.Purchases.Commands;

public class HoldSeatHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task Available_seat_is_held_for_what_is_left_of_the_session()
    {
        var session = Session();
        var seat = Seat();
        var sessions = new FakePurchaseSessionStore(session);
        var repository = new FakeEventSeatRepository(seat);
        var locks = new FakeEventSeatLockStore();

        var before = DateTime.UtcNow;
        var result = await Handler(sessions, repository, locks).HandleAsync(Command(session, seat.Id), CancellationToken.None);
        var after = DateTime.UtcNow;

        Assert.Equal(HoldSeatStatus.Held, result.Status);
        Assert.Equal("A-1", result.Seat!.SeatNumber);
        Assert.Equal(seat.Id, result.Seat.EventSeatId);
        Assert.Equal(EventId, result.Seat.EventId);

        // The hold always reports the session's own deadline (ADR-001).
        Assert.Equal(session.ExpiresAtUtc, result.Seat.ExpiresAtUtc);
        Assert.InRange(
            locks.LastDuration!.Value,
            session.ExpiresAtUtc - after,
            session.ExpiresAtUtc - before);

        Assert.Equal(session.Id.ToString(), locks.GetHolderIdentifier(seat.Id));
        Assert.Equal([seat.Id], session.HeldEventSeatIds);
        Assert.Equal(1, sessions.SaveCalls);
    }

    [Fact]
    public async Task Holding_a_second_seat_does_not_renew_the_session()
    {
        var session = Session();
        var expiresAtUtc = session.ExpiresAtUtc;
        var first = Seat();
        var second = Seat();
        var sessions = new FakePurchaseSessionStore(session);
        var locks = new FakeEventSeatLockStore();
        var handler = Handler(sessions, new FakeEventSeatRepository(first, second), locks);

        await handler.HandleAsync(Command(session, first.Id), CancellationToken.None);
        var firstDuration = locks.LastDuration!.Value;
        var result = await handler.HandleAsync(Command(session, second.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.Held, result.Status);
        Assert.Equal(expiresAtUtc, session.ExpiresAtUtc);
        Assert.Equal(expiresAtUtc, result.Seat!.ExpiresAtUtc);

        // The window shrinks as the session runs out; it is never a fresh SessionDuration.
        Assert.True(locks.LastDuration!.Value <= firstDuration);
        Assert.Equal([first.Id, second.Id], session.HeldEventSeatIds);
    }

    [Fact]
    public async Task Holding_the_same_seat_again_changes_nothing()
    {
        var session = Session();
        var expiresAtUtc = session.ExpiresAtUtc;
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        var handler = Handler(new FakePurchaseSessionStore(session), new FakeEventSeatRepository(seat), locks);

        await handler.HandleAsync(Command(session, seat.Id), CancellationToken.None);
        var result = await handler.HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.Held, result.Status);
        Assert.Equal([seat.Id], session.HeldEventSeatIds);
        Assert.Equal(expiresAtUtc, session.ExpiresAtUtc);
        Assert.Equal(session.Id.ToString(), locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public async Task Seat_held_by_another_purchase_is_rejected_and_the_seats_already_held_stay_held()
    {
        var session = Session();
        var held = Seat();
        var contested = Seat();
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(contested.Id, Guid.NewGuid().ToString(), SessionDuration);
        var handler = Handler(new FakePurchaseSessionStore(session), new FakeEventSeatRepository(held, contested), locks);
        await handler.HandleAsync(Command(session, held.Id), CancellationToken.None);

        var result = await handler.HandleAsync(Command(session, contested.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.HeldByAnotherPurchase, result.Status);
        Assert.Equal([held.Id], session.HeldEventSeatIds);
        Assert.NotEqual(session.Id.ToString(), locks.GetHolderIdentifier(contested.Id));
    }

    [Fact]
    public async Task Going_past_the_seat_limit_holds_nothing()
    {
        var session = Session();
        var held = Seat();
        var refused = Seat();
        var sessions = new FakePurchaseSessionStore(session);
        var locks = new FakeEventSeatLockStore();
        var handler = Handler(sessions, new FakeEventSeatRepository(held, refused), locks, maxSeats: 1);
        await handler.HandleAsync(Command(session, held.Id), CancellationToken.None);
        locks.ResetAcquireCalls();

        var result = await handler.HandleAsync(Command(session, refused.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.SeatLimitReached, result.Status);
        Assert.Equal([held.Id], session.HeldEventSeatIds);
        Assert.Equal(0, locks.AcquireCalls);
        Assert.Null(locks.GetHolderIdentifier(refused.Id));
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

        var result = await Handler(new FakePurchaseSessionStore(session), repository, locks, catalog: catalog)
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.SessionExpired, result.Status);

        // Redis before PostgreSQL (ADR-002): a dead session never reaches the database.
        Assert.Empty(catalog.RequestedIds);
        Assert.Empty(repository.RequestedIds);

        // The contract has Booking free the held seats as soon as it notices the expiration.
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public async Task Unknown_session_is_rejected_without_querying_postgres()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat);
        var catalog = new FakeEventCatalog(EventId);

        var result = await Handler(new FakePurchaseSessionStore(), repository, new FakeEventSeatLockStore(), catalog: catalog)
            .HandleAsync(new HoldSeatCommand(Guid.NewGuid(), EventId, seat.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.SessionExpired, result.Status);
        Assert.Empty(catalog.RequestedIds);
        Assert.Empty(repository.RequestedIds);
    }

    [Fact]
    public async Task Seat_of_another_event_is_rejected_without_holding_it()
    {
        var session = Session();
        var held = Seat();
        var elsewhere = Seat(OtherEventId);
        var locks = new FakeEventSeatLockStore();
        var handler = Handler(new FakePurchaseSessionStore(session), new FakeEventSeatRepository(held, elsewhere), locks);
        await handler.HandleAsync(Command(session, held.Id), CancellationToken.None);

        var result = await handler.HandleAsync(
            new HoldSeatCommand(session.Id, OtherEventId, elsewhere.Id),
            CancellationToken.None);

        Assert.Equal(HoldSeatStatus.Invalid, result.Status);
        Assert.Equal("eventId", Assert.Single(result.Errors.Keys));
        Assert.Equal([held.Id], session.HeldEventSeatIds);
        Assert.Null(locks.GetHolderIdentifier(elsewhere.Id));
    }

    [Fact]
    public async Task Unknown_seat_holds_nothing()
    {
        var session = Session();
        var locks = new FakeEventSeatLockStore();

        var result = await Handler(new FakePurchaseSessionStore(session), new FakeEventSeatRepository(Seat()), locks)
            .HandleAsync(Command(session, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.EventSeatNotFound, result.Status);
        Assert.Empty(session.HeldEventSeatIds);
        Assert.Empty(locks.Holders);
    }

    [Fact]
    public async Task Unknown_event_returns_not_found()
    {
        var session = Session();
        var seat = Seat();

        var result = await Handler(
                new FakePurchaseSessionStore(session),
                new FakeEventSeatRepository(seat),
                new FakeEventSeatLockStore(),
                catalog: new FakeEventCatalog())
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.EventNotFound, result.Status);
        Assert.Empty(session.HeldEventSeatIds);
    }

    [Fact]
    public async Task Sold_seat_is_rejected()
    {
        var session = Session();
        var seat = Seat();
        var buyer = Session();
        buyer.Hold(EventId, [seat.Id], 1, DateTime.UtcNow);
        seat.Sell(buyer, DateTime.UtcNow);
        var locks = new FakeEventSeatLockStore();

        var result = await Handler(new FakePurchaseSessionStore(session), new FakeEventSeatRepository(seat), locks)
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.AlreadySold, result.Status);
        Assert.Empty(session.HeldEventSeatIds);
        Assert.Empty(locks.Holders);
    }

    [Fact]
    public async Task Sale_landing_after_the_lock_releases_it_and_holds_nothing()
    {
        var session = Session();
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat)
        {
            AfterSecondRead = sold => sold!.Sell(session, DateTime.UtcNow)
        };
        var locks = new FakeEventSeatLockStore();

        var result = await Handler(new FakePurchaseSessionStore(session), repository, locks)
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.AlreadySold, result.Status);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
        Assert.Empty(session.HeldEventSeatIds);
    }

    [Fact]
    public async Task Seat_vanishing_after_the_lock_releases_it_and_holds_nothing()
    {
        var session = Session();
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat) { ReturnNullAfterSecondRead = true };
        var locks = new FakeEventSeatLockStore();

        var result = await Handler(new FakePurchaseSessionStore(session), repository, locks)
            .HandleAsync(Command(session, seat.Id), CancellationToken.None);

        Assert.Equal(HoldSeatStatus.EventSeatNotFound, result.Status);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
        Assert.Empty(session.HeldEventSeatIds);
    }

    [Fact]
    public async Task Seat_with_a_missing_venue_seat_is_never_held()
    {
        var session = Session();
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        var handler = new HoldSeatHandler(
            new FakePurchaseSessionStore(session),
            new FakeEventCatalog(EventId),
            new FakeEventSeatRepository(seat),
            new FakeVenueSeatCatalog(),
            locks,
            new PurchaseSessionOptions { SessionDuration = SessionDuration });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(Command(session, seat.Id), CancellationToken.None));

        Assert.Empty(locks.Holders);
        Assert.Empty(session.HeldEventSeatIds);
    }

    private static HoldSeatCommand Command(Purchase session, Guid eventSeatId) =>
        new(session.Id, EventId, eventSeatId);

    private static HoldSeatHandler Handler(
        FakePurchaseSessionStore sessions,
        FakeEventSeatRepository repository,
        FakeEventSeatLockStore locks,
        int maxSeats = 3,
        FakeEventCatalog? catalog = null) =>
        new(sessions,
            catalog ?? new FakeEventCatalog(EventId, OtherEventId),
            repository,
            VenueCatalogOf(repository.Seats),
            locks,
            new PurchaseSessionOptions { SessionDuration = SessionDuration, MaxSeats = maxSeats });

    private static Purchase Session() =>
        Purchase.Start("Juan Perez", "juan.perez@example.com", DateTime.UtcNow, SessionDuration);

    /// <summary>A session that is still stored but whose deadline already passed, holding the given seat.</summary>
    private static Purchase ExpiredSession(Guid heldEventSeatId)
    {
        var startedAtUtc = DateTime.UtcNow - SessionDuration - TimeSpan.FromMinutes(1);
        var session = Purchase.Start("Juan Perez", "juan.perez@example.com", startedAtUtc, SessionDuration);
        session.Hold(EventId, [heldEventSeatId], 3, startedAtUtc);
        return session;
    }

    private static EventSeat Seat(Guid? eventId = null) =>
        EventSeat.CreateAvailable(eventId ?? EventId, Guid.NewGuid(), ZoneId);

    // Seat numbers follow the order the seats were given to the repository: the first one is A-1.
    private static FakeVenueSeatCatalog VenueCatalogOf(IReadOnlyCollection<EventSeat> seats) =>
        new(seats.Select((seat, index) => new VenueSeat(seat.VenueSeatId, ZoneId, $"A-{index + 1}")).ToArray());
}
