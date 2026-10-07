using BookingService.Application.Tests.TestDoubles;
using BookingService.Application.Tickets;
using BookingService.Domain;

namespace BookingService.Application.Tests.Tickets;

public class ReserveSeatHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);

    private readonly Ticket _seatA1 = TestTickets.Create(KnownEventId, "A-1");
    private readonly Ticket _soldSeat = TestTickets.Create(KnownEventId, "A-2", TicketStatus.Sold);
    private readonly FakeUserRepository _users = new();
    private readonly FakeSeatLockStore _seatLocks = new();
    private readonly ReserveSeatHandler _handler;

    public ReserveSeatHandlerTests()
    {
        var events = new FakeEventRepository(KnownEventId);
        _handler = new ReserveSeatHandler(
            new TicketPurchaseValidator(events),
            new FakeTicketRepository(_seatA1, _soldSeat),
            _users,
            new FakeUnitOfWork(),
            _seatLocks,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Available_seat_is_locked_for_the_new_buyer_during_ten_minutes()
    {
        var result = await _handler.HandleAsync(Command(_seatA1.Id, "juan.perez@example.com"));

        Assert.Equal(ReserveSeatStatus.Reserved, result.Status);
        var buyer = Assert.Single(_users.Users);
        Assert.Equal("juan.perez@example.com", buyer.Email);
        Assert.Equal(buyer.Id, result.BuyerId);
        Assert.Equal(buyer.Id, await _seatLocks.GetHolderAsync(_seatA1.Id));
        Assert.Equal(Now.UtcDateTime.AddMinutes(10), result.ExpiresAtUtc);
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
    }

    [Fact]
    public async Task Same_buyer_can_reserve_again_and_is_not_duplicated()
    {
        await _handler.HandleAsync(Command(_seatA1.Id, "juan.perez@example.com"));

        var result = await _handler.HandleAsync(Command(_seatA1.Id, " juan.perez@example.com "));

        Assert.Equal(ReserveSeatStatus.Reserved, result.Status);
        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task Seat_reserved_by_another_buyer_is_locked()
    {
        await _handler.HandleAsync(Command(_seatA1.Id, "juan.perez@example.com"));

        var result = await _handler.HandleAsync(Command(_seatA1.Id, "ana.lopez@example.com"));

        Assert.Equal(ReserveSeatStatus.LockedByAnotherUser, result.Status);
        Assert.Equal(_users.Users[0].Id, await _seatLocks.GetHolderAsync(_seatA1.Id));
    }

    [Fact]
    public async Task Seat_can_be_reserved_by_another_buyer_once_the_lock_expires()
    {
        await _handler.HandleAsync(Command(_seatA1.Id, "juan.perez@example.com"));
        _seatLocks.Expire(_seatA1.Id);

        var result = await _handler.HandleAsync(Command(_seatA1.Id, "ana.lopez@example.com"));

        Assert.Equal(ReserveSeatStatus.Reserved, result.Status);
    }

    [Fact]
    public async Task Sold_seat_cannot_be_reserved()
    {
        var result = await _handler.HandleAsync(Command(_soldSeat.Id, "juan.perez@example.com"));

        Assert.Equal(ReserveSeatStatus.AlreadySold, result.Status);
        Assert.Null(await _seatLocks.GetHolderAsync(_soldSeat.Id));
    }

    [Fact]
    public async Task Unknown_seat_is_not_found()
    {
        var result = await _handler.HandleAsync(Command(Guid.NewGuid(), "juan.perez@example.com"));

        Assert.Equal(ReserveSeatStatus.TicketNotFound, result.Status);
    }

    [Fact]
    public async Task Unknown_event_is_not_found()
    {
        var result = await _handler.HandleAsync(Command(_seatA1.Id, "juan.perez@example.com") with { EventId = Guid.NewGuid() });

        Assert.Equal(ReserveSeatStatus.EventNotFound, result.Status);
    }

    [Fact]
    public async Task Invalid_buyer_data_is_invalid_and_nothing_is_locked()
    {
        var result = await _handler.HandleAsync(Command(_seatA1.Id, "not-an-email") with { FullName = "" });

        Assert.Equal(ReserveSeatStatus.Invalid, result.Status);
        Assert.Equal(["email", "fullName"], result.Errors.Keys.Order());
        Assert.Null(await _seatLocks.GetHolderAsync(_seatA1.Id));
        Assert.Empty(_users.Users);
    }

    private static ReserveSeatCommand Command(Guid ticketId, string email) =>
        new(KnownEventId, ticketId, "Juan Perez", email);
}
