using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.Queries;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Venues;

namespace BookingService.Application.Tests.EventSeats.Queries;

public class GetAvailableEventSeatsHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ZoneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VenueSeatA1 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VenueSeatA2 = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid VenueSeatA10 = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void Returns_available_seats_sorted_ordinal_ignore_case()
    {
        var repository = new FakeRepository(
            Seat("A-2"), Seat("A-10"), SoldSeat("B-1"));
        var venue = new FakeVenueCatalog(repository.Seats);
        var result = Handler(repository, venue).Handle(new(EventId));

        Assert.Equal(["A-10", "A-2"], result!.Select(s => s.SeatNumber));
    }

    [Fact]
    public void Cache_hit_skips_event_and_repository_but_still_filters_locks()
    {
        var seat = Seat("A-1");
        var cache = new FakeCache { Values = [new AvailableEventSeat(seat.Id, "A-1")] };
        var locks = new FakeLockStore();
        locks.TryAcquire(seat.Id, "buyer", TimeSpan.FromMinutes(10));
        var repository = new FakeRepository(seat);
        var result = new GetAvailableEventSeatsHandler(repository, new FakeEventCatalog(), cache, locks, new FakeVenueCatalog(seat))
            .Handle(new(EventId));

        Assert.Empty(result!);
        Assert.Equal(0, repository.GetByEventCalls);
        Assert.Equal(0, cache.SetCalls);
    }

    [Fact]
    public void Cache_miss_sets_unfiltered_values_and_later_changes_are_hidden()
    {
        var seat = Seat("A-1");
        var repository = new FakeRepository(seat);
        var cache = new FakeCache();
        var handler = Handler(repository, new FakeVenueCatalog(seat), cache, new FakeLockStore());
        var first = handler.Handle(new(EventId));
        seat.Sell("Jane", "jane@example.com", DateTime.UtcNow);
        var second = handler.Handle(new(EventId));

        Assert.Single(first!);
        Assert.Single(second!);
        Assert.Equal(1, cache.SetCalls);
    }

    [Fact]
    public void Unknown_event_returns_null_without_caching()
    {
        var cache = new FakeCache();
        var result = Handler(new FakeRepository(), new FakeVenueCatalog(), cache, new FakeLockStore(), false).Handle(new(EventId));

        Assert.Null(result);
        Assert.Equal(0, cache.SetCalls);
    }

    [Fact]
    public void All_sold_returns_empty_and_caches_empty_list()
    {
        var seat = SoldSeat("A-1");
        var cache = new FakeCache();
        var result = Handler(new FakeRepository(seat), new FakeVenueCatalog(seat), cache, new FakeLockStore()).Handle(new(EventId));

        Assert.Empty(result!);
        Assert.Equal(1, cache.SetCalls);
        Assert.Empty(cache.Values!);
    }

    [Fact]
    public void Missing_venue_seat_throws_without_setting_cache()
    {
        var seat = Seat("A-1");
        var cache = new FakeCache();

        Assert.Throws<InvalidOperationException>(() => Handler(new FakeRepository(seat), new FakeVenueCatalog(), cache, new FakeLockStore()).Handle(new(EventId)));
        Assert.Equal(0, cache.SetCalls);
    }

    private static EventSeat Seat(string number) => EventSeat.CreateAvailable(EventId, number switch
    {
        "A-2" => VenueSeatA2,
        "A-10" => VenueSeatA10,
        _ => VenueSeatA1
    }, ZoneId);
    private static EventSeat SoldSeat(string number) { var seat = Seat(number); seat.Sell("Jane", "jane@example.com", DateTime.UtcNow); return seat; }
    private static GetAvailableEventSeatsHandler Handler(FakeRepository repo, FakeVenueCatalog venue, FakeCache? cache = null, FakeLockStore? locks = null, bool known = true) =>
        new(repo, new FakeEventCatalog(known ? EventId : Guid.Empty), cache ?? new FakeCache(), locks ?? new FakeLockStore(), venue);

    private sealed class FakeEventCatalog(params Guid[] ids) : IEventCatalog { private readonly HashSet<Guid> _ids = ids.ToHashSet(); public bool Exists(Guid id) => _ids.Contains(id); }
    private sealed class FakeVenueCatalog(params EventSeat[] seats) : IVenueSeatCatalog
    {
        private readonly Dictionary<Guid, VenueSeat> _seats = seats.ToDictionary(s => s.VenueSeatId, s => new VenueSeat(s.VenueSeatId, s.ZoneId, Number(s)));
        private static string Number(EventSeat seat) => seat.VenueSeatId == VenueSeatA10 ? "A-10" : seat.VenueSeatId == VenueSeatA2 ? "A-2" : "A-1";
        public VenueSeat? GetById(Guid id) => _seats.GetValueOrDefault(id);
        public IReadOnlyCollection<VenueSeat> GetByIds(IReadOnlyCollection<Guid> ids) => ids.Where(_seats.ContainsKey).Select(id => _seats[id]).ToArray();
    }
    private sealed class FakeRepository(params EventSeat[] seats) : IEventSeatRepository
    {
        public EventSeat[] Seats { get; } = seats;
        public int GetByEventCalls { get; private set; }
        public IReadOnlyCollection<EventSeat> GetByEvent(Guid id) { GetByEventCalls++; return Seats.Where(s => s.EventId == id).ToArray(); }
        public EventSeat? GetById(Guid eventId, Guid eventSeatId) => Seats.FirstOrDefault(s => s.EventId == eventId && s.Id == eventSeatId);
        public EventSeatSale? GetSaleByIdempotencyKey(Guid key, Guid eventId, Guid eventSeatId) => null;
        public EventSeatSaleOutcome SellOnce(Guid key, Guid eventId, Guid eventSeatId, Func<EventSeat, BookingService.Domain.Tickets.Ticket> sell) => throw new NotSupportedException();
    }
    private sealed class FakeLockStore : IEventSeatLockStore
    {
        private readonly HashSet<Guid> _locked = [];
        public EventSeatLockResult TryAcquire(Guid id, string holder, TimeSpan duration) { _locked.Add(id); return EventSeatLockResult.Acquired; }
        public string? GetHolderIdentifier(Guid id) => _locked.Contains(id) ? "holder" : null;
        public IReadOnlySet<Guid> GetLockedEventSeatIds(IReadOnlyCollection<Guid> ids) => ids.Where(_locked.Contains).ToHashSet();
        public bool Release(Guid id, string holder) => _locked.Remove(id);
    }
    private sealed class FakeCache : IAvailableEventSeatsCache
    {
        public IReadOnlyList<AvailableEventSeat>? Values { get; set; }
        public int SetCalls { get; private set; }
        public IReadOnlyList<AvailableEventSeat>? Get(Guid id) => Values;
        public void Set(Guid id, IReadOnlyList<AvailableEventSeat> seats) { SetCalls++; Values = seats; }
        public void Invalidate(Guid id) => Values = null;
    }
}
