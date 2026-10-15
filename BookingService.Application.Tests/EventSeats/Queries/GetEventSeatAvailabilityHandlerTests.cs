using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.Queries;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Venues;

namespace BookingService.Application.Tests.EventSeats.Queries;

public class GetEventSeatAvailabilityHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ZoneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VenueSeatId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData(false, false, EventSeatAvailabilityStatus.Available)]
    [InlineData(false, true, EventSeatAvailabilityStatus.Reserved)]
    [InlineData(true, true, EventSeatAvailabilityStatus.Sold)]
    public void Returns_status_with_sold_winning_over_lock(bool sold, bool locked, EventSeatAvailabilityStatus expected)
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
        if (sold) seat.Sell("Jane", "jane@example.com", DateTime.UtcNow);
        var locks = new FakeLockStore(locked ? seat.Id : null);
        var result = new GetEventSeatAvailabilityHandler(new FakeRepository(seat), new FakeEventCatalog(EventId), locks, new FakeVenueCatalog())
            .Handle(new(EventId, seat.Id));

        Assert.NotNull(result);
        Assert.Equal(seat.Id, result.EventSeatId);
        Assert.Equal("A-1", result.SeatNumber);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Unknown_event_or_seat_returns_null()
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
        var handler = new GetEventSeatAvailabilityHandler(new FakeRepository(seat), new FakeEventCatalog(EventId), new FakeLockStore(), new FakeVenueCatalog());

        Assert.Null(handler.Handle(new(Guid.NewGuid(), seat.Id)));
        Assert.Null(handler.Handle(new(EventId, Guid.NewGuid())));
    }

    [Fact]
    public void Missing_venue_seat_throws()
    {
        var seat = EventSeat.CreateAvailable(EventId, Guid.NewGuid(), ZoneId);

        Assert.Throws<InvalidOperationException>(() => new GetEventSeatAvailabilityHandler(new FakeRepository(seat), new FakeEventCatalog(EventId), new FakeLockStore(), new FakeVenueCatalog()).Handle(new(EventId, seat.Id)));
    }

    private sealed class FakeEventCatalog(params Guid[] ids) : IEventCatalog { private readonly HashSet<Guid> _ids = ids.ToHashSet(); public bool Exists(Guid id) => _ids.Contains(id); }
    private sealed class FakeVenueCatalog : IVenueSeatCatalog
    {
        public VenueSeat? GetById(Guid id) => id == VenueSeatId ? new VenueSeat(id, ZoneId, "A-1") : null;
        public IReadOnlyCollection<VenueSeat> GetByIds(IReadOnlyCollection<Guid> ids) => ids.Select(id => GetById(id)).OfType<VenueSeat>().ToArray();
    }
    private sealed class FakeRepository(params EventSeat[] seats) : IEventSeatRepository
    {
        public IReadOnlyCollection<EventSeat> GetByEvent(Guid id) => seats.Where(s => s.EventId == id).ToArray();
        public EventSeat? GetById(Guid eventId, Guid eventSeatId) => seats.FirstOrDefault(s => s.EventId == eventId && s.Id == eventSeatId);
        public EventSeatSale? GetSaleByIdempotencyKey(Guid key, Guid eventId, Guid eventSeatId) => null;
        public EventSeatSaleOutcome SellOnce(Guid key, Guid eventId, Guid eventSeatId, Func<EventSeat, BookingService.Domain.Tickets.Ticket> sell) => throw new NotSupportedException();
    }
    private sealed class FakeLockStore(Guid? locked = null) : IEventSeatLockStore
    {
        private readonly Guid? _locked = locked;
        public EventSeatLockResult TryAcquire(Guid id, string holder, TimeSpan duration) => EventSeatLockResult.Acquired;
        public string? GetHolderIdentifier(Guid id) => _locked == id ? "holder" : null;
        public IReadOnlySet<Guid> GetLockedEventSeatIds(IReadOnlyCollection<Guid> ids) => ids.Where(id => id == _locked).ToHashSet();
        public bool Release(Guid id, string holder) => false;
    }
}
