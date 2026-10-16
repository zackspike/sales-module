using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.Commands;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Application.Tests.EventSeats.Fakes;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Tickets;
using BookingService.Domain.Venues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Application.Tests.EventSeats.Commands;

public class PurchaseEventSeatHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VenueSeatId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Purchase_issues_ticket_and_releases_the_lock()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat);
        var locks = BuyerLock(seat);
        var key = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var result = Handler(repository, locks).Handle(Command(seat.Id, key, " Jane Doe ", " JANE@example.com "));
        var after = DateTime.UtcNow;

        Assert.Equal(PurchaseEventSeatStatus.Purchased, result.Status);
        Assert.NotNull(result.Ticket);
        Assert.Equal("Jane Doe", result.Ticket.HolderName);
        Assert.Equal("JANE@example.com", result.Ticket.HolderEmail);
        Assert.Matches("^TK-[0-9a-f]{32}$", result.Ticket.TicketCode);
        Assert.InRange(result.Ticket.SoldAtUtc, before, after);
        Assert.Equal(EventSeatStatus.Sold, seat.Status);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public void Purchase_remembers_key_and_invalidates_event_cache()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat);
        var locks = BuyerLock(seat);
        var idempotency = new FakeIdempotencyStore();
        var cache = new FakeAvailableEventSeatsCache();
        var key = Guid.NewGuid();

        var result = Handler(repository, locks, idempotency, cache: cache).Handle(Command(seat.Id, key));

        Assert.Equal(PurchaseEventSeatStatus.Purchased, result.Status);
        Assert.Equal(new IdempotencyRecord(EventId, seat.Id), idempotency.Records[key].Record);
        Assert.Equal(PurchaseEventSeatHandler.IdempotencyKeyRetention, idempotency.Records[key].Retention);
        Assert.Equal(1, cache.InvalidateCalls);
    }

    [Fact]
    public void Invalid_input_is_rejected_before_event_lookup()
    {
        var catalog = new FakeEventCatalog(EventId);
        var repository = new FakeEventSeatRepository();
        var result = new PurchaseEventSeatHandler(
            catalog,
            repository,
            new FakeVenueSeatCatalog(),
            new FakeEventSeatLockStore(),
            new FakeIdempotencyStore(),
            new FakeAvailableEventSeatsCache(),
            NullLogger<PurchaseEventSeatHandler>.Instance).Handle(
                Command(Guid.NewGuid(), Guid.Empty, "", "bad"));

        Assert.Equal(PurchaseEventSeatStatus.Invalid, result.Status);
        Assert.Empty(catalog.RequestedIds);
        Assert.Contains("fullName", result.Errors.Keys);
        Assert.Contains("email", result.Errors.Keys);
    }

    [Fact]
    public void Empty_idempotency_key_is_invalid()
    {
        var result = Handler(new FakeEventSeatRepository(Seat()), new FakeEventSeatLockStore())
            .Handle(Command(Guid.NewGuid(), Guid.Empty));

        Assert.Equal(PurchaseEventSeatStatus.Invalid, result.Status);
        Assert.Contains("idempotencyKey", result.Errors.Keys);
    }

    [Fact]
    public void Unknown_event_returns_not_found_before_reading_the_seat()
    {
        var catalog = new FakeEventCatalog();
        var repository = new FakeEventSeatRepository();
        var result = new PurchaseEventSeatHandler(
            catalog,
            repository,
            new FakeVenueSeatCatalog(),
            new FakeEventSeatLockStore(),
            new FakeIdempotencyStore(),
            new FakeAvailableEventSeatsCache(),
            NullLogger<PurchaseEventSeatHandler>.Instance).Handle(
                new(OtherEventId, Guid.NewGuid(), "Jane Doe", "jane@example.com", Guid.NewGuid()));

        Assert.Equal(PurchaseEventSeatStatus.EventNotFound, result.Status);
        Assert.Empty(repository.RequestedIds);
    }

    [Fact]
    public void Existing_event_with_unknown_seat_returns_not_found()
    {
        var result = Handler(new FakeEventSeatRepository(), new FakeEventSeatLockStore())
            .Handle(Command(Guid.NewGuid()));

        Assert.Equal(PurchaseEventSeatStatus.EventSeatNotFound, result.Status);
    }

    [Fact]
    public void Seat_disappearing_before_sell_returns_not_found()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat)
        {
            RemoveSeatBeforeSellOnce = true
        };

        var result = Handler(repository, BuyerLock(seat)).Handle(Command(seat.Id));

        Assert.Equal(PurchaseEventSeatStatus.EventSeatNotFound, result.Status);
    }

    [Fact]
    public void Remembered_key_for_another_event_is_a_conflict()
    {
        var seat = Seat();
        var key = Guid.NewGuid();
        var idempotency = new FakeIdempotencyStore();
        idempotency.Records[key] = (
            new IdempotencyRecord(OtherEventId, seat.Id),
            PurchaseEventSeatHandler.IdempotencyKeyRetention);

        var result = Handler(
            new FakeEventSeatRepository(seat),
            new FakeEventSeatLockStore(),
            idempotency)
            .Handle(Command(seat.Id, key));

        Assert.Equal(PurchaseEventSeatStatus.IdempotencyKeyConflict, result.Status);
    }

    [Fact]
    public void Missing_lock_requires_reservation()
    {
        var seat = Seat();

        var result = Handler(new FakeEventSeatRepository(seat), new FakeEventSeatLockStore())
            .Handle(Command(seat.Id));

        Assert.Equal(PurchaseEventSeatStatus.ReservationRequired, result.Status);
        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }

    [Fact]
    public void Other_holder_lock_requires_reservation()
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "other@example.com", TimeSpan.FromMinutes(10));

        var result = Handler(new FakeEventSeatRepository(seat), locks)
            .Handle(Command(seat.Id, Guid.NewGuid(), "Jane", " JANE@example.com "));

        Assert.Equal(PurchaseEventSeatStatus.ReservationRequired, result.Status);
        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }

    [Fact]
    public void Remembered_purchase_replays_the_original_ticket()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat);
        var idempotency = new FakeIdempotencyStore();
        var key = Guid.NewGuid();
        var locks = BuyerLock(seat);
        var first = Handler(repository, locks, idempotency).Handle(Command(seat.Id, key));

        var replay = Handler(repository, locks, idempotency).Handle(
            Command(seat.Id, key, "Different", "different@example.com"));

        Assert.Equal(PurchaseEventSeatStatus.Purchased, first.Status);
        Assert.Equal(PurchaseEventSeatStatus.Replayed, replay.Status);
        Assert.Equal(first.Ticket, replay.Ticket);
    }

    [Fact]
    public void Remembered_purchase_for_another_seat_is_a_conflict()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat);
        var idempotency = new FakeIdempotencyStore();
        var key = Guid.NewGuid();
        Handler(repository, BuyerLock(seat), idempotency).Handle(Command(seat.Id, key));

        var result = Handler(repository, BuyerLock(seat), idempotency)
            .Handle(Command(Guid.NewGuid(), key));

        Assert.Equal(PurchaseEventSeatStatus.IdempotencyKeyConflict, result.Status);
    }

    [Fact]
    public void Repository_replay_for_another_seat_is_an_idempotency_conflict()
    {
        var seat = Seat();
        var otherSeat = Seat();
        var key = Guid.NewGuid();
        var repository = new FakeEventSeatRepository(seat, otherSeat);
        repository.RememberSale(key, otherSeat);

        var result = Handler(repository, BuyerLock(seat)).Handle(Command(seat.Id, key));

        Assert.Equal(PurchaseEventSeatStatus.IdempotencyKeyConflict, result.Status);
        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }

    [Fact]
    public void Sold_seat_replays_from_repository_after_cache_expiry()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat);
        var idempotency = new FakeIdempotencyStore();
        var key = Guid.NewGuid();
        Handler(repository, BuyerLock(seat), idempotency).Handle(Command(seat.Id, key));
        idempotency.Records.Clear();

        var result = Handler(repository, new FakeEventSeatLockStore(), idempotency)
            .Handle(Command(seat.Id, key));

        Assert.Equal(PurchaseEventSeatStatus.Replayed, result.Status);
    }

    [Fact]
    public void Different_key_on_sold_seat_is_already_sold()
    {
        var seat = Seat();
        seat.Sell("Jane", "jane@example.com", DateTime.UtcNow);

        var result = Handler(new FakeEventSeatRepository(seat), new FakeEventSeatLockStore())
            .Handle(Command(seat.Id));

        Assert.Equal(PurchaseEventSeatStatus.AlreadySold, result.Status);
    }

    [Fact]
    public void Missing_sale_after_remembered_record_returns_not_found()
    {
        var seat = Seat();
        var idempotency = new FakeIdempotencyStore();
        var key = Guid.NewGuid();
        idempotency.Records[key] = (new IdempotencyRecord(EventId, seat.Id), TimeSpan.FromMinutes(10));

        var result = Handler(new FakeEventSeatRepository(seat), new FakeEventSeatLockStore(), idempotency)
            .Handle(Command(seat.Id, key));

        Assert.Equal(PurchaseEventSeatStatus.EventSeatNotFound, result.Status);
    }

    [Fact]
    public void Lock_mismatch_with_sold_re_read_returns_replayed_for_matching_key()
    {
        var seat = Seat();
        var key = Guid.NewGuid();
        var repository = new FakeEventSeatRepository(seat);
        repository.AfterSecondRead = current =>
        {
            if (current is not null)
            {
                repository.RememberSale(key, current);
            }
        };
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "other@example.com", TimeSpan.FromMinutes(10));

        var result = Handler(repository, locks).Handle(Command(seat.Id, key));

        Assert.Equal(PurchaseEventSeatStatus.Replayed, result.Status);
    }

    [Fact]
    public void Lock_mismatch_with_sold_re_read_returns_already_sold_for_other_key()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat)
        {
            AfterSecondRead = current => current?.Sell("other", "other@example.com", DateTime.UtcNow)
        };
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "other@example.com", TimeSpan.FromMinutes(10));

        var result = Handler(repository, locks).Handle(Command(seat.Id));

        Assert.Equal(PurchaseEventSeatStatus.AlreadySold, result.Status);
    }

    [Fact]
    public void Already_sold_exception_is_translated_to_already_sold()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat) { ThrowAlreadySold = true };

        var result = Handler(repository, BuyerLock(seat)).Handle(Command(seat.Id));

        Assert.Equal(PurchaseEventSeatStatus.AlreadySold, result.Status);
    }

    [Fact]
    public void Post_commit_failure_is_logged_and_purchase_remains_successful()
    {
        var seat = Seat();
        var idempotency = new FakeIdempotencyStore { ThrowOnRemember = true };
        var logger = new FakeLogger();

        var result = Handler(new FakeEventSeatRepository(seat), BuyerLock(seat), idempotency, logger: logger)
            .Handle(Command(seat.Id));

        Assert.Equal(PurchaseEventSeatStatus.Purchased, result.Status);
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public void Missing_venue_seat_is_not_sold()
    {
        var seat = Seat();
        var locks = BuyerLock(seat);

        Assert.Throws<InvalidOperationException>(() => Handler(
            new FakeEventSeatRepository(seat),
            locks,
            venue: new FakeVenueSeatCatalog()).Handle(Command(seat.Id)));
        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }

    private static PurchaseEventSeatCommand Command(
        Guid seatId,
        Guid? key = null,
        string name = "Jane Doe",
        string email = "jane@example.com") =>
        new(EventId, seatId, name, email, key ?? Guid.NewGuid());

    private static FakeEventSeatLockStore BuyerLock(EventSeat seat)
    {
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "jane@example.com", TimeSpan.FromMinutes(10));
        return locks;
    }

    private static PurchaseEventSeatHandler Handler(
        FakeEventSeatRepository repository,
        FakeEventSeatLockStore locks,
        FakeIdempotencyStore? idempotency = null,
        ILogger<PurchaseEventSeatHandler>? logger = null,
        FakeVenueSeatCatalog? venue = null,
        FakeAvailableEventSeatsCache? cache = null,
        FakeEventCatalog? catalog = null) =>
        new(
            catalog ?? new FakeEventCatalog(EventId),
            repository,
            venue ?? new FakeVenueSeatCatalog(new VenueSeat(VenueSeatId, ZoneId, "A-1")),
            locks,
            idempotency ?? new FakeIdempotencyStore(),
            cache ?? new FakeAvailableEventSeatsCache(),
            logger ?? NullLogger<PurchaseEventSeatHandler>.Instance);

    private static EventSeat Seat() => EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);

    private sealed class FakeLogger : ILogger<PurchaseEventSeatHandler>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
