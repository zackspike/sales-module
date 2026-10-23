using BookingService.Application.EventSeats.Queries;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Application.Tests.EventSeats.Fakes;
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
    private static readonly Guid VenueSeatB1 = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task Returns_available_seats_sorted_ordinal_ignore_case()
    {
        var seats = new[] { Seat("a-2"), Seat("A-10"), Seat("B-1") };
        var repository = new FakeEventSeatRepository(seats);
        var result = await Handler(repository, VenueCatalog(seats)).HandleAsync(new(EventId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(["A-10", "a-2", "B-1"], result.Select(seat => seat.SeatNumber));
    }

    [Fact]
    public async Task Cache_hit_skips_event_and_repository_but_still_filters_locks()
    {
        var seat = Seat("A-1");
        var cache = new FakeAvailableEventSeatsCache();
        cache.Values[EventId] = [new AvailableEventSeat(seat.Id, "A-1")];
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "buyer", TimeSpan.FromMinutes(10));
        var repository = new FakeEventSeatRepository(seat);
        var catalog = new FakeEventCatalog();

        var result = await Handler(repository, VenueCatalog(seat), cache, locks, catalog).HandleAsync(new(EventId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
        Assert.Equal(0, repository.GetByEventCalls);
        Assert.Empty(catalog.RequestedIds);
        Assert.Equal(0, cache.SetCalls);
    }

    [Fact]
    public async Task Cache_miss_excludes_locked_seat_but_caches_it()
    {
        var seat = Seat("A-1");
        var repository = new FakeEventSeatRepository(seat);
        var cache = new FakeAvailableEventSeatsCache();
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "buyer@example.com", TimeSpan.FromMinutes(10));

        var first = await Handler(repository, VenueCatalog(seat), cache, locks).HandleAsync(new(EventId), CancellationToken.None);
        Assert.NotNull(first);
        Assert.Empty(first);
        Assert.Contains(cache.Values[EventId], cached => cached.EventSeatId == seat.Id);
        Assert.Equal(1, cache.SetCalls);
    }

    [Fact]
    public async Task Cached_seats_are_returned_after_repository_seat_is_sold()
    {
        var seat = Seat("A-1");
        var repository = new FakeEventSeatRepository(seat);
        var cache = new FakeAvailableEventSeatsCache();
        var locks = new FakeEventSeatLockStore();
        var handler = Handler(repository, VenueCatalog(seat), cache, locks);

        var first = await handler.HandleAsync(new(EventId), CancellationToken.None);
        seat.Sell("Jane", "jane@example.com", DateTime.UtcNow);
        var second = await handler.HandleAsync(new(EventId), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Single(first);
        Assert.NotNull(second);
        Assert.Single(second);
        Assert.Equal(1, repository.GetByEventCalls);
    }

    [Fact]
    public async Task Unknown_event_returns_null_without_caching()
    {
        var cache = new FakeAvailableEventSeatsCache();
        var catalog = new FakeEventCatalog();
        var result = await Handler(
            new FakeEventSeatRepository(),
            new FakeVenueSeatCatalog(),
            cache,
            new FakeEventSeatLockStore(),
            catalog)
            .HandleAsync(new(EventId), CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(cache.Values);
        Assert.Contains(EventId, catalog.RequestedIds);
    }

    [Fact]
    public async Task All_sold_returns_empty_and_caches_empty_list()
    {
        var seat = SoldSeat("A-1");
        var cache = new FakeAvailableEventSeatsCache();

        var result = await Handler(new FakeEventSeatRepository(seat), VenueCatalog(seat), cache, new FakeEventSeatLockStore())
            .HandleAsync(new(EventId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
        Assert.Equal(1, cache.SetCalls);
        Assert.Empty(cache.Values[EventId]);
    }

    [Fact]
    public async Task Missing_venue_seat_throws_without_setting_cache()
    {
        var seat = Seat("A-1");
        var cache = new FakeAvailableEventSeatsCache();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(
            new FakeEventSeatRepository(seat),
            new FakeVenueSeatCatalog(),
            cache,
            new FakeEventSeatLockStore()).HandleAsync(new(EventId), CancellationToken.None));
        Assert.Equal(0, cache.SetCalls);
    }

    private static EventSeat Seat(string number) => EventSeat.CreateAvailable(
        EventId,
        number switch
        {
            "a-2" or "A-2" => VenueSeatA2,
            "A-10" => VenueSeatA10,
            "B-1" => VenueSeatB1,
            _ => VenueSeatA1
        },
        ZoneId);

    private static EventSeat SoldSeat(string number)
    {
        var seat = Seat(number);
        seat.Sell("Jane", "jane@example.com", DateTime.UtcNow);
        return seat;
    }

    private static FakeVenueSeatCatalog VenueCatalog(params EventSeat[] seats) =>
        new(seats.Select(seat => new VenueSeat(seat.VenueSeatId, ZoneId, SeatNumber(seat))).ToArray());

    private static string SeatNumber(EventSeat seat) => seat.VenueSeatId switch
    {
        var id when id == VenueSeatA2 => "a-2",
        var id when id == VenueSeatA10 => "A-10",
        var id when id == VenueSeatB1 => "B-1",
        _ => "A-1"
    };

    private static GetAvailableEventSeatsHandler Handler(
        FakeEventSeatRepository repository,
        FakeVenueSeatCatalog venue,
        FakeAvailableEventSeatsCache? cache = null,
        FakeEventSeatLockStore? locks = null,
        FakeEventCatalog? catalog = null) =>
        new(
            repository,
            catalog ?? new FakeEventCatalog(EventId),
            cache ?? new FakeAvailableEventSeatsCache(),
            locks ?? new FakeEventSeatLockStore(),
            venue);
}
