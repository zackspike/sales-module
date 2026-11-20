using BookingService.Application.EventSeats.Queries;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Application.Tests.EventSeats.Fakes;
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
    [InlineData(true, false, EventSeatAvailabilityStatus.Sold)]
    [InlineData(true, true, EventSeatAvailabilityStatus.Sold)]
    public async Task Returns_status_with_sold_winning_over_lock(
        bool sold,
        bool locked,
        EventSeatAvailabilityStatus expected)
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
        if (sold)
        {
            seat.Sell("Jane", "jane@example.com", DateTime.UtcNow);
        }

        var locks = new FakeEventSeatLockStore();
        if (locked)
        {
            locks.TryAcquire(seat.Id, "holder", TimeSpan.FromMinutes(10));
        }

        var result = await Handler(seat, locks).HandleAsync(new(EventId, seat.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(seat.Id, result.EventSeatId);
        Assert.Equal("A-1", result.SeatNumber);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Unknown_event_returns_null()
    {
        var seat = Seat();

        var result = await Handler(seat, new FakeEventSeatLockStore()).HandleAsync(new(Guid.NewGuid(), seat.Id), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Unknown_seat_returns_null()
    {
        var result = await Handler(Seat(), new FakeEventSeatLockStore()).HandleAsync(new(EventId, Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Missing_venue_seat_throws()
    {
        var seat = EventSeat.CreateAvailable(EventId, Guid.NewGuid(), ZoneId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new GetEventSeatAvailabilityHandler(
            new FakeEventSeatRepository(seat),
            new FakeEventCatalog(EventId),
            new FakeEventSeatLockStore(),
            new FakeVenueSeatCatalog()).HandleAsync(new(EventId, seat.Id), CancellationToken.None));
    }

    private static EventSeat Seat() => EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);

    private static GetEventSeatAvailabilityHandler Handler(
        EventSeat seat,
        FakeEventSeatLockStore locks) =>
        new(
            new FakeEventSeatRepository(seat),
            new FakeEventCatalog(EventId),
            locks,
            new FakeVenueSeatCatalog(new VenueSeat(VenueSeatId, ZoneId, "A-1")));
}
