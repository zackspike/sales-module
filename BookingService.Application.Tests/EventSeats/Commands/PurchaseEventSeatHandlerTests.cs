using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.Commands;
using BookingService.Application.EventSeats.ReadModels;
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
    public void Purchase_commits_ticket_and_updates_all_stores()
    {
        var seat = Seat();
        var repository = new FakeRepository(seat);
        var locks = new FakeLockStore();
        var key = Guid.NewGuid();
        locks.TryAcquire(seat.Id, "jane@example.com", TimeSpan.FromMinutes(10));

        var result = Handler(repository, locks).Handle(Command(seat.Id, key, " Jane Doe ", " JANE@example.com "));

        Assert.Equal(PurchaseEventSeatStatus.Purchased, result.Status);
        Assert.NotNull(result.Ticket);
        Assert.Equal("Jane Doe", result.Ticket.HolderName);
        Assert.Equal("JANE@example.com", result.Ticket.HolderEmail);
        Assert.Matches("^TK-[0-9a-f]{32}$", result.Ticket.TicketCode);
        Assert.Equal(EventSeatStatus.Sold, seat.Status);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
        Assert.Equal(new IdempotencyRecord(EventId, seat.Id), repository.Idempotency.Records[key].Record);
        Assert.Equal(PurchaseEventSeatHandler.IdempotencyKeyRetention, repository.Idempotency.Records[key].Retention);
        Assert.Equal(1, repository.Cache.InvalidateCalls);
    }

    [Fact]
    public void Invalid_input_is_rejected_before_event_lookup()
    {
        var catalog = new FakeEventCatalog(EventId);
        var repository = new FakeRepository();
        var result = new PurchaseEventSeatHandler(catalog, repository, new FakeVenueCatalog(), new FakeLockStore(), repository.Idempotency, repository.Cache, NullLogger<PurchaseEventSeatHandler>.Instance)
            .Handle(Command(Guid.NewGuid(), Guid.Empty, "", "bad"));

        Assert.Equal(PurchaseEventSeatStatus.Invalid, result.Status);
        Assert.Empty(catalog.Requested);
        Assert.Contains("fullName", result.Errors.Keys);
        Assert.Contains("email", result.Errors.Keys);
    }

    [Fact]
    public void Empty_idempotency_key_is_invalid()
    {
        var seat = Seat();
        var result = Handler(new FakeRepository(seat), new FakeLockStore()).Handle(Command(seat.Id, Guid.Empty));

        Assert.Equal(PurchaseEventSeatStatus.Invalid, result.Status);
        Assert.Contains("idempotencyKey", result.Errors.Keys);
    }

    [Fact]
    public void Missing_or_other_holder_lock_requires_reservation()
    {
        var seat = Seat();
        var locks = new FakeLockStore();
        var handler = Handler(new FakeRepository(seat), locks);
        Assert.Equal(PurchaseEventSeatStatus.ReservationRequired, handler.Handle(Command(seat.Id)).Status);
        locks.TryAcquire(seat.Id, "other@example.com", TimeSpan.FromMinutes(10));
        Assert.Equal(PurchaseEventSeatStatus.ReservationRequired, handler.Handle(Command(seat.Id, Guid.NewGuid(), "Jane", " JANE@example.com ")).Status);
        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }

    [Fact]
    public void Remembered_purchase_replays_or_conflicts()
    {
        var seat = Seat();
        var repository = new FakeRepository(seat);
        var key = Guid.NewGuid();
        var locks = new FakeLockStore();
        locks.TryAcquire(seat.Id, "jane@example.com", TimeSpan.FromMinutes(10));
        var first = Handler(repository, locks).Handle(Command(seat.Id, key));
        var replay = Handler(repository, locks).Handle(Command(seat.Id, key, "Different", "different@example.com"));
        var conflict = Handler(repository, locks).Handle(Command(Guid.NewGuid(), key));

        Assert.Equal(PurchaseEventSeatStatus.Purchased, first.Status);
        Assert.Equal(PurchaseEventSeatStatus.Replayed, replay.Status);
        Assert.Equal(first.Ticket, replay.Ticket);
        Assert.Equal(PurchaseEventSeatStatus.IdempotencyKeyConflict, conflict.Status);
    }

    [Fact]
    public void Sold_seat_replays_from_repository_after_cache_expiry()
    {
        var seat = Seat();
        var repository = new FakeRepository(seat);
        var key = Guid.NewGuid();
        var locks = new FakeLockStore();
        locks.TryAcquire(seat.Id, "jane@example.com", TimeSpan.FromMinutes(10));
        Handler(repository, locks).Handle(Command(seat.Id, key));
        repository.Idempotency.Records.Clear();

        var result = Handler(repository, locks).Handle(Command(seat.Id, key));

        Assert.Equal(PurchaseEventSeatStatus.Replayed, result.Status);
    }

    [Fact]
    public void Different_key_on_sold_seat_is_already_sold()
    {
        var seat = Seat();
        seat.Sell("Jane", "jane@example.com", DateTime.UtcNow);

        Assert.Equal(PurchaseEventSeatStatus.AlreadySold, Handler(new FakeRepository(seat), new FakeLockStore()).Handle(Command(seat.Id)).Status);
    }

    [Fact]
    public void Missing_sale_after_remembered_record_returns_not_found()
    {
        var seat = Seat();
        var repository = new FakeRepository(seat);
        var key = Guid.NewGuid();
        repository.Idempotency.Records[key] = (new IdempotencyRecord(EventId, seat.Id), TimeSpan.FromMinutes(10));

        Assert.Equal(PurchaseEventSeatStatus.EventSeatNotFound, Handler(repository, new FakeLockStore()).Handle(Command(seat.Id, key)).Status);
    }

    [Fact]
    public void Post_commit_failure_is_logged_and_purchase_remains_successful()
    {
        var seat = Seat();
        var repository = new FakeRepository(seat) { Idempotency = new FakeIdempotencyStore { ThrowOnRemember = true } };
        var locks = new FakeLockStore();
        locks.TryAcquire(seat.Id, "jane@example.com", TimeSpan.FromMinutes(10));
        var logger = new FakeLogger();

        var result = Handler(repository, locks, logger).Handle(Command(seat.Id));

        Assert.Equal(PurchaseEventSeatStatus.Purchased, result.Status);
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public void Missing_venue_seat_is_not_sold()
    {
        var seat = Seat();
        var locks = new FakeLockStore();
        locks.TryAcquire(seat.Id, "jane@example.com", TimeSpan.FromMinutes(10));

        Assert.Throws<InvalidOperationException>(() => Handler(new FakeRepository(seat), locks, venue: new FakeVenueCatalog()).Handle(Command(seat.Id)));
        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }

    [Fact]
    public void Concurrent_purchases_sell_once()
    {
        var seat = Seat();
        var repository = new FakeRepository(seat);
        var locks = new FakeLockStore();
        var handlers = Enumerable.Range(0, 8).Select(_ =>
        {
            locks.TryAcquire(seat.Id, "jane@example.com", TimeSpan.FromMinutes(10));
            return Handler(repository, locks);
        }).ToArray();

        var results = handlers.Select((handler, index) => Task.Run(() => handler.Handle(Command(seat.Id, Guid.NewGuid())))).Select(t => t.Result).ToArray();

        Assert.Equal(1, results.Count(r => r.Status == PurchaseEventSeatStatus.Purchased));
        Assert.Equal(7, results.Count(r => r.Status == PurchaseEventSeatStatus.AlreadySold));
    }

    private static PurchaseEventSeatCommand Command(Guid seatId, Guid? key = null, string name = "Jane Doe", string email = "jane@example.com") => new(EventId, seatId, name, email, key ?? Guid.NewGuid());
    private static PurchaseEventSeatHandler Handler(FakeRepository repository, FakeLockStore locks, FakeLogger? logger = null, FakeVenueCatalog? venue = null) =>
        new(new FakeEventCatalog(EventId), repository, venue ?? new FakeVenueCatalog(VenueSeatId), locks, repository.Idempotency, repository.Cache, logger ?? new FakeLogger());
    private static EventSeat Seat() => EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);

    private sealed class FakeEventCatalog(params Guid[] ids) : IEventCatalog { public List<Guid> Requested { get; } = []; private readonly HashSet<Guid> _ids = ids.ToHashSet(); public bool Exists(Guid id) { Requested.Add(id); return _ids.Contains(id); } }
    private sealed class FakeVenueCatalog(params Guid[] ids) : IVenueSeatCatalog
    {
        private readonly Dictionary<Guid, VenueSeat> _seats = ids.ToDictionary(id => id, id => new VenueSeat(id, ZoneId, "A-1"));
        public VenueSeat? GetById(Guid id) => _seats.GetValueOrDefault(id);
        public IReadOnlyCollection<VenueSeat> GetByIds(IReadOnlyCollection<Guid> ids) => ids.Where(_seats.ContainsKey).Select(id => _seats[id]).ToArray();
    }
    private sealed class FakeLockStore : IEventSeatLockStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, string> _holders = [];
        public EventSeatLockResult TryAcquire(Guid id, string holder, TimeSpan duration) { lock (_sync) { if (!_holders.TryGetValue(id, out var current)) { _holders[id] = holder; return EventSeatLockResult.Acquired; } return current == holder ? EventSeatLockResult.Renewed : EventSeatLockResult.HeldByAnother; } }
        public string? GetHolderIdentifier(Guid id) { lock (_sync) return _holders.GetValueOrDefault(id); }
        public IReadOnlySet<Guid> GetLockedEventSeatIds(IReadOnlyCollection<Guid> ids) { lock (_sync) return ids.Where(_holders.ContainsKey).ToHashSet(); }
        public bool Release(Guid id, string holder) { lock (_sync) return _holders.Remove(id); }
    }
    private sealed class FakeRepository(params EventSeat[] initial) : IEventSeatRepository
    {
        private readonly object _sync = new();
        private readonly List<EventSeat> _seats = initial.ToList();
        private readonly Dictionary<Guid, EventSeatSale> _sales = [];
        private readonly Dictionary<Guid, EventSeatSale> _keys = [];
        public FakeIdempotencyStore Idempotency { get; set; } = new();
        public FakeCache Cache { get; } = new();
        public IReadOnlyCollection<EventSeat> GetByEvent(Guid id) { lock (_sync) return _seats.Where(s => s.EventId == id).ToArray(); }
        public EventSeat? GetById(Guid eventId, Guid eventSeatId) { lock (_sync) return _seats.FirstOrDefault(s => s.EventId == eventId && s.Id == eventSeatId); }
        public EventSeatSale? GetSaleByIdempotencyKey(Guid key, Guid eventId, Guid eventSeatId) { lock (_sync) return _keys.GetValueOrDefault(key) is { } sale && sale.Seat.EventId == eventId && sale.Seat.Id == eventSeatId ? sale : null; }
        public EventSeatSaleOutcome SellOnce(Guid key, Guid eventId, Guid eventSeatId, Func<EventSeat, Ticket> sell) { lock (_sync) { if (_keys.TryGetValue(key, out var replay)) return new(replay, true); var seat = _seats.FirstOrDefault(s => s.EventId == eventId && s.Id == eventSeatId); if (seat is null) return new(null, false); var ticket = sell(seat); var sale = new EventSeatSale(seat, ticket); _sales[seat.Id] = sale; _keys[key] = sale; return new(sale, false); } }
    }
    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        public Dictionary<Guid, (IdempotencyRecord Record, TimeSpan Retention)> Records { get; } = [];
        public bool ThrowOnRemember { get; init; }
        public IdempotencyRecord? GetIdempotencyRecord(Guid key) => Records.GetValueOrDefault(key).Record;
        public void Remember(Guid key, IdempotencyRecord record, TimeSpan retention) { if (ThrowOnRemember) throw new InvalidOperationException("store unavailable"); Records[key] = (record, retention); }
    }
    private sealed class FakeCache : IAvailableEventSeatsCache { public int InvalidateCalls { get; private set; } public IReadOnlyList<AvailableEventSeat>? Get(Guid id) => null; public void Set(Guid id, IReadOnlyList<AvailableEventSeat> seats) { } public void Invalidate(Guid id) => InvalidateCalls++; }
    private sealed class FakeLogger : ILogger<PurchaseEventSeatHandler>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}
