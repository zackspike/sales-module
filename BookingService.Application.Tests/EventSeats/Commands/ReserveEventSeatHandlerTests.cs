using BookingService.Application.EventSeats.Commands;
using BookingService.Application.Tests.EventSeats.Fakes;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Venues;

namespace BookingService.Application.Tests.EventSeats.Commands;

public class ReserveEventSeatHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VenueSeatId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Available_seat_is_reserved_with_normalized_holder()
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        var before = DateTime.UtcNow;
        var result = Handler(new FakeEventSeatRepository(seat), locks)
            .Handle(Command(seat.Id, "Jane Doe", " JUAN.Perez@Example.com "));
        var after = DateTime.UtcNow;

        Assert.Equal(ReserveEventSeatStatus.Reserved, result.Status);
        Assert.NotNull(result.Seat);
        Assert.Equal(seat.Id, result.Seat.EventSeatId);
        Assert.Equal(EventId, result.Seat.EventId);
        Assert.Equal("A-1", result.Seat.SeatNumber);
        Assert.InRange(
            result.Seat.ExpiresAtUtc,
            before + EventSeatReservationPolicy.Duration,
            after + EventSeatReservationPolicy.Duration);
        Assert.Equal(EventSeatReservationPolicy.Duration, locks.LastDuration);
        Assert.Equal("juan.perez@example.com", locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public void Invalid_holder_does_not_touch_repository_or_locks()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat);
        var locks = new FakeEventSeatLockStore();

        var result = Handler(repository, locks).Handle(Command(seat.Id, "", "bad"));

        Assert.Equal(ReserveEventSeatStatus.Invalid, result.Status);
        Assert.Empty(repository.RequestedIds);
        Assert.Empty(locks.Holders);
    }

    [Fact]
    public void Unknown_event_returns_not_found()
    {
        var seat = Seat();

        var result = Handler(new FakeEventSeatRepository(seat), new FakeEventSeatLockStore(), false)
            .Handle(Command(seat.Id));

        Assert.Equal(ReserveEventSeatStatus.EventNotFound, result.Status);
    }

    [Fact]
    public void Unknown_seat_returns_not_found()
    {
        var result = Handler(new FakeEventSeatRepository(Seat()), new FakeEventSeatLockStore())
            .Handle(Command(Guid.NewGuid()));

        Assert.Equal(ReserveEventSeatStatus.EventSeatNotFound, result.Status);
    }

    [Fact]
    public void Sold_seat_is_rejected()
    {
        var seat = Seat();
        seat.Sell("Old", "old@example.com", DateTime.UtcNow);

        var result = Handler(new FakeEventSeatRepository(seat), new FakeEventSeatLockStore())
            .Handle(Command(seat.Id));

        Assert.Equal(ReserveEventSeatStatus.AlreadySold, result.Status);
    }

    [Fact]
    public void Another_holder_is_not_replaced()
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "other@example.com", TimeSpan.FromMinutes(10));

        var result = Handler(new FakeEventSeatRepository(seat), locks).Handle(Command(seat.Id));

        Assert.Equal(ReserveEventSeatStatus.ReservedByAnotherBuyer, result.Status);
        Assert.Equal("other@example.com", locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public void Same_holder_renews_the_reservation()
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "juan@example.com", TimeSpan.FromMinutes(10));
        locks.ResetAcquireCalls();

        var result = Handler(new FakeEventSeatRepository(seat), locks)
            .Handle(Command(seat.Id, "Juan", " JUAN@Example.com "));

        Assert.Equal(ReserveEventSeatStatus.Reserved, result.Status);
        Assert.Equal(1, locks.AcquireCalls);
        Assert.Equal(EventSeatReservationPolicy.Duration, locks.LastDuration);
        Assert.Equal("juan@example.com", locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public void Missing_venue_seat_throws_before_locking()
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();

        Assert.Throws<InvalidOperationException>(() => new ReserveEventSeatHandler(
            new FakeEventCatalog(EventId),
            new FakeEventSeatRepository(seat),
            new FakeVenueSeatCatalog(),
            locks).Handle(Command(seat.Id)));
        Assert.Empty(locks.Holders);
    }

    [Fact]
    public void Sale_after_lock_is_rejected_and_lock_is_released()
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        var repository = new FakeEventSeatRepository(seat)
        {
            AfterSecondRead = current => current?.Sell("buyer", "buyer@example.com", DateTime.UtcNow)
        };

        var result = Handler(repository, locks).Handle(Command(seat.Id));

        Assert.Equal(ReserveEventSeatStatus.AlreadySold, result.Status);
        Assert.Equal(1, locks.AcquireCalls);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public void Seat_disappearing_after_lock_is_not_found_and_lock_is_released()
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        var repository = new FakeEventSeatRepository(seat) { ReturnNullAfterSecondRead = true };

        var result = Handler(repository, locks).Handle(Command(seat.Id));

        Assert.Equal(ReserveEventSeatStatus.EventSeatNotFound, result.Status);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
    }

    private static ReserveEventSeatCommand Command(
        Guid seatId,
        string name = "Jane Doe",
        string email = "jane@example.com") =>
        new(EventId, seatId, name, email);

    private static ReserveEventSeatHandler Handler(
        FakeEventSeatRepository repository,
        FakeEventSeatLockStore locks,
        bool known = true) =>
        new(
            known ? new FakeEventCatalog(EventId) : new FakeEventCatalog(),
            repository,
            new FakeVenueSeatCatalog(new VenueSeat(VenueSeatId, ZoneId, "A-1")),
            locks);

    private static EventSeat Seat() => EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
}
