using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.Commands;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Venues;

namespace BookingService.Application.Tests.EventSeats.Commands;

public class ReserveEventSeatHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VenueSeatId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Available_seat_is_reserved_with_normalized_holder()
    {
        var seat = Seat();
        var locks = new FakeLockStore();
        var before = DateTime.UtcNow;
        var result = Handler(seat, locks).Handle(Command(EventId, seat.Id, "Jane Doe", " JUAN.Perez@Example.com "));
        var after = DateTime.UtcNow;

        Assert.Equal(ReserveEventSeatStatus.Reserved, result.Status);
        Assert.NotNull(result.Seat);
        Assert.Equal(seat.Id, result.Seat.EventSeatId);
        Assert.Equal(EventId, result.Seat.EventId);
        Assert.Equal("A-1", result.Seat.SeatNumber);
        Assert.InRange(result.Seat.ExpiresAtUtc, before + EventSeatReservationPolicy.Duration, after + EventSeatReservationPolicy.Duration);
        Assert.Equal("juan.perez@example.com", locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public void Invalid_holder_does_not_touch_dependencies()
    {
        var seat = Seat();
        var repository = new FakeRepository(seat);
        var locks = new FakeLockStore();
        var result = new ReserveEventSeatHandler(new FakeEventCatalog(EventId), repository, new FakeVenueCatalog(VenueSeatId), locks)
            .Handle(Command(EventId, seat.Id, "", "bad"));

        Assert.Equal(ReserveEventSeatStatus.Invalid, result.Status);
        Assert.Empty(repository.RequestedIds);
        Assert.Empty(locks.Holders);
    }

    [Fact]
    public void Unknown_event_or_seat_returns_not_found()
    {
        var seat = Seat();
        var repository = new FakeRepository(seat);

        Assert.Equal(ReserveEventSeatStatus.EventNotFound, Handler(seat, new FakeLockStore(), false).Handle(Command(EventId, seat.Id)).Status);
        Assert.Equal(ReserveEventSeatStatus.EventSeatNotFound, Handler(repository, new FakeLockStore()).Handle(Command(EventId, Guid.NewGuid())).Status);
        Assert.Equal(ReserveEventSeatStatus.EventSeatNotFound, Handler(new FakeRepository(EventSeat.CreateAvailable(OtherEventId, VenueSeatId, ZoneId)), new FakeLockStore()).Handle(Command()).Status);
    }

    [Fact]
    public void Sold_seat_and_another_holder_are_rejected()
    {
        var sold = Seat();
        sold.Sell("Old", "old@example.com", DateTime.UtcNow);
        Assert.Equal(ReserveEventSeatStatus.AlreadySold, Handler(sold, new FakeLockStore()).Handle(Command(EventId, sold.Id)).Status);

        var available = Seat();
        var locks = new FakeLockStore();
        locks.TryAcquire(available.Id, "other@example.com", TimeSpan.FromMinutes(10));
        var result = Handler(available, locks).Handle(Command(EventId, available.Id));

        Assert.Equal(ReserveEventSeatStatus.ReservedByAnotherBuyer, result.Status);
        Assert.Equal("other@example.com", locks.GetHolderIdentifier(available.Id));
    }

    [Fact]
    public void Same_holder_renews_the_reservation()
    {
        var seat = Seat();
        var locks = new FakeLockStore();
        locks.TryAcquire(seat.Id, "juan@example.com", TimeSpan.FromMinutes(10));

        var result = Handler(seat, locks).Handle(Command(EventId, seat.Id, "Juan", " JUAN@Example.com "));

        Assert.Equal(ReserveEventSeatStatus.Reserved, result.Status);
        Assert.Equal(2, locks.AcquireCalls);
        Assert.Equal("juan@example.com", locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public void Missing_venue_seat_throws_before_locking()
    {
        var seat = Seat();
        var locks = new FakeLockStore();

        Assert.Throws<InvalidOperationException>(() => new ReserveEventSeatHandler(
            new FakeEventCatalog(EventId), new FakeRepository(seat), new FakeVenueCatalog(), locks).Handle(Command(EventId, seat.Id)));
        Assert.Empty(locks.Holders);
    }

    [Fact]
    public void Sale_after_lock_is_rejected_and_lock_is_released()
    {
        var seat = Seat();
        var locks = new FakeLockStore();
        var repository = new FakeRepository(seat) { AfterFirstRead = () => seat.Sell("buyer", "buyer@example.com", DateTime.UtcNow) };

        var result = Handler(repository, locks).Handle(Command(EventId, seat.Id));

        Assert.Equal(ReserveEventSeatStatus.AlreadySold, result.Status);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
    }

    private static ReserveEventSeatCommand Command(Guid eventId = default, Guid seatId = default, string name = "Jane Doe", string email = "jane@example.com") =>
        new(eventId == default ? EventId : eventId, seatId == default ? Guid.Empty : seatId, name, email);

    private static ReserveEventSeatHandler Handler(EventSeat seat, FakeLockStore locks, bool known = true) =>
        Handler(new FakeRepository(seat), locks, known);

    private static ReserveEventSeatHandler Handler(FakeRepository repository, FakeLockStore locks, bool known = true) =>
        new(new FakeEventCatalog(known ? EventId : Guid.Empty), repository, new FakeVenueCatalog(VenueSeatId), locks);

    private static EventSeat Seat() => EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);

    private sealed class FakeEventCatalog(params Guid[] ids) : IEventCatalog
    {
        private readonly HashSet<Guid> _ids = ids.ToHashSet();
        public bool Exists(Guid eventId) => _ids.Contains(eventId);
    }

    private sealed class FakeVenueCatalog(params Guid[] ids) : IVenueSeatCatalog
    {
        private readonly Dictionary<Guid, VenueSeat> _seats = ids.ToDictionary(id => id, id => new VenueSeat(id, ZoneId, "A-1"));
        public VenueSeat? GetById(Guid id) => _seats.GetValueOrDefault(id);
        public IReadOnlyCollection<VenueSeat> GetByIds(IReadOnlyCollection<Guid> ids) => ids.Where(_seats.ContainsKey).Select(id => _seats[id]).ToArray();
    }

    private sealed class FakeRepository(params EventSeat[] seats) : IEventSeatRepository
    {
        private readonly List<EventSeat> _seats = seats.ToList();
        public List<Guid> RequestedIds { get; } = [];
        public Action? AfterFirstRead { get; init; }
        public IReadOnlyCollection<EventSeat> GetByEvent(Guid eventId) => _seats.Where(s => s.EventId == eventId).ToArray();
        public EventSeat? GetById(Guid eventId, Guid eventSeatId) { RequestedIds.Add(eventSeatId); var seat = _seats.FirstOrDefault(s => s.EventId == eventId && s.Id == eventSeatId); if (RequestedIds.Count == 1) AfterFirstRead?.Invoke(); return seat; }
        public EventSeatSale? GetSaleByIdempotencyKey(Guid key, Guid eventId, Guid eventSeatId) => null;
        public EventSeatSaleOutcome SellOnce(Guid key, Guid eventId, Guid eventSeatId, Func<EventSeat, BookingService.Domain.Tickets.Ticket> sell) => throw new NotSupportedException();
    }

    private sealed class FakeLockStore : IEventSeatLockStore
    {
        public Dictionary<Guid, string> Holders { get; } = [];
        public int AcquireCalls { get; private set; }
        public EventSeatLockResult TryAcquire(Guid id, string holder, TimeSpan duration) { AcquireCalls++; if (!Holders.TryGetValue(id, out var current)) { Holders[id] = holder; return EventSeatLockResult.Acquired; } return current == holder ? EventSeatLockResult.Renewed : EventSeatLockResult.HeldByAnother; }
        public string? GetHolderIdentifier(Guid id) => Holders.GetValueOrDefault(id);
        public IReadOnlySet<Guid> GetLockedEventSeatIds(IReadOnlyCollection<Guid> ids) => ids.Where(Holders.ContainsKey).ToHashSet();
        public bool Release(Guid id, string holder) => Holders.Remove(id, out var current) && current == holder;
    }
}
