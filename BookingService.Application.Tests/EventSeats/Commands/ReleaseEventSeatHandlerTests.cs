using BookingService.Application.EventSeats.Commands;
using BookingService.Application.Tests.EventSeats.Fakes;
using BookingService.Domain.EventSeats;

namespace BookingService.Application.Tests.EventSeats.Commands;

public class ReleaseEventSeatHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VenueSeatId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Holder_releases_the_reservation()
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "token:jane@example.com", TimeSpan.FromMinutes(10));

        var result = await Handler(new FakeEventSeatRepository(seat), locks)
            .HandleAsync(Command(seat.Id, " JANE@Example.com "), CancellationToken.None);

        Assert.Equal(ReleaseEventSeatStatus.Released, result.Status);
        Assert.Null(locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public async Task Seat_without_reservation_is_released()
    {
        var seat = Seat();

        var result = await Handler(new FakeEventSeatRepository(seat), new FakeEventSeatLockStore())
            .HandleAsync(Command(seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseEventSeatStatus.Released, result.Status);
    }

    [Fact]
    public async Task Another_holder_is_not_released()
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "other-token:other@example.com", TimeSpan.FromMinutes(10));

        var result = await Handler(new FakeEventSeatRepository(seat), locks).HandleAsync(Command(seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseEventSeatStatus.ReservedByAnotherBuyer, result.Status);
        Assert.Equal("other-token:other@example.com", locks.GetHolderIdentifier(seat.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("stolen-guess")]
    public async Task Same_email_without_the_buyer_token_is_not_released(string? token)
    {
        var seat = Seat();
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "token:jane@example.com", TimeSpan.FromMinutes(10));

        var result = await Handler(new FakeEventSeatRepository(seat), locks)
            .HandleAsync(Command(seat.Id, token: token), CancellationToken.None);

        Assert.Equal(ReleaseEventSeatStatus.ReservedByAnotherBuyer, result.Status);
        Assert.Equal("token:jane@example.com", locks.GetHolderIdentifier(seat.Id));
    }

    [Fact]
    public async Task Sold_seat_is_rejected()
    {
        var seat = Seat();
        seat.Sell("Old", "old@example.com", DateTime.UtcNow);

        var result = await Handler(new FakeEventSeatRepository(seat), new FakeEventSeatLockStore())
            .HandleAsync(Command(seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseEventSeatStatus.AlreadySold, result.Status);
    }

    [Fact]
    public async Task Unknown_event_returns_not_found()
    {
        var seat = Seat();

        var result = await Handler(new FakeEventSeatRepository(seat), new FakeEventSeatLockStore(), false)
            .HandleAsync(Command(seat.Id), CancellationToken.None);

        Assert.Equal(ReleaseEventSeatStatus.EventNotFound, result.Status);
    }

    [Fact]
    public async Task Unknown_seat_returns_not_found()
    {
        var result = await Handler(new FakeEventSeatRepository(Seat()), new FakeEventSeatLockStore())
            .HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ReleaseEventSeatStatus.EventSeatNotFound, result.Status);
    }

    [Fact]
    public async Task Invalid_email_does_not_touch_repository_or_locks()
    {
        var seat = Seat();
        var repository = new FakeEventSeatRepository(seat);
        var locks = new FakeEventSeatLockStore();
        locks.TryAcquire(seat.Id, "token:jane@example.com", TimeSpan.FromMinutes(10));

        var result = await Handler(repository, locks).HandleAsync(Command(seat.Id, "bad"), CancellationToken.None);

        Assert.Equal(ReleaseEventSeatStatus.Invalid, result.Status);
        Assert.Equal("email", Assert.Single(result.Errors.Keys));
        Assert.Empty(repository.RequestedIds);
        Assert.Equal("token:jane@example.com", locks.GetHolderIdentifier(seat.Id));
    }

    private static ReleaseEventSeatCommand Command(Guid seatId, string email = "jane@example.com", string? token = "token") =>
        new(EventId, seatId, email, token);

    private static ReleaseEventSeatHandler Handler(
        FakeEventSeatRepository repository,
        FakeEventSeatLockStore locks,
        bool known = true) =>
        new(known ? new FakeEventCatalog(EventId) : new FakeEventCatalog(), repository, locks);

    private static EventSeat Seat() => EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
}
