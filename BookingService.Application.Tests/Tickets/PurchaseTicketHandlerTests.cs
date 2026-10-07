using BookingService.Application.Tests.TestDoubles;
using BookingService.Application.Tickets;
using BookingService.Domain;

namespace BookingService.Application.Tests.Tickets;

public class PurchaseTicketHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Ticket _seatA1 = TestTickets.Create(KnownEventId, "A-1");
    private readonly Ticket _seatA2 = TestTickets.Create(KnownEventId, "A-2");
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly PurchaseTicketHandler _handler;

    public PurchaseTicketHandlerTests()
    {
        _handler = CreateHandler(new FakeEventRepository(KnownEventId), new FakeTicketRepository(_seatA1, _seatA2));
    }

    [Fact]
    public async Task Available_seat_is_purchased()
    {
        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.Purchased, result.Status);
        Assert.Same(_seatA1, result.Ticket);
        Assert.Equal(TicketStatus.Sold, _seatA1.Status);
        Assert.Equal("Juan Perez", _seatA1.User!.FullName);
        Assert.Equal("juan.perez@example.com", _seatA1.User.Email);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task New_buyer_is_registered_once_and_reused()
    {
        await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));
        await _handler.HandleAsync(Command(_seatA2.Id, Guid.NewGuid()));

        var buyer = Assert.Single(_users.Users);
        Assert.Same(buyer, _seatA1.User);
        Assert.Same(buyer, _seatA2.User);
    }

    [Fact]
    public async Task Retry_with_same_key_returns_original_purchase()
    {
        var key = Guid.NewGuid();
        var first = await _handler.HandleAsync(Command(_seatA1.Id, key));
        var code = first.Ticket!.TicketCode;

        var retry = await _handler.HandleAsync(Command(_seatA1.Id, key));

        Assert.Equal(PurchaseTicketStatus.Replayed, retry.Status);
        Assert.Same(_seatA1, retry.Ticket);
        Assert.Equal(code, retry.Ticket!.TicketCode);
    }

    [Fact]
    public async Task Sold_seat_with_new_key_is_already_sold()
    {
        await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.AlreadySold, result.Status);
        Assert.Null(result.Ticket);
    }

    [Fact]
    public async Task Key_used_for_another_seat_is_a_conflict()
    {
        var key = Guid.NewGuid();
        await _handler.HandleAsync(Command(_seatA1.Id, key));

        var result = await _handler.HandleAsync(Command(_seatA2.Id, key));

        Assert.Equal(PurchaseTicketStatus.IdempotencyKeyConflict, result.Status);
        Assert.Equal(TicketStatus.Available, _seatA2.Status);
    }

    [Fact]
    public async Task Unknown_seat_is_not_found()
    {
        var result = await _handler.HandleAsync(Command(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.TicketNotFound, result.Status);
    }

    [Fact]
    public async Task Seat_of_another_event_is_not_found()
    {
        var otherEventId = Guid.NewGuid();
        var handler = CreateHandler(new FakeEventRepository(KnownEventId, otherEventId), new FakeTicketRepository(_seatA1));

        var result = await handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()) with { EventId = otherEventId });

        Assert.Equal(PurchaseTicketStatus.TicketNotFound, result.Status);
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
    }

    [Fact]
    public async Task Unknown_event_is_not_found()
    {
        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()) with { EventId = UnknownEventId });

        Assert.Equal(PurchaseTicketStatus.EventNotFound, result.Status);
    }

    [Fact]
    public async Task Invalid_customer_data_is_invalid_and_seat_stays_available()
    {
        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()) with { FullName = " ", Email = "abc" });

        Assert.Equal(PurchaseTicketStatus.Invalid, result.Status);
        Assert.Equal(["email", "fullName"], result.Errors.Keys.Order());
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_invalid()
    {
        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.Empty));

        Assert.Equal(PurchaseTicketStatus.Invalid, result.Status);
        Assert.Equal(["idempotencyKey"], result.Errors.Keys);
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
    }

    [Fact]
    public async Task Customer_data_is_trimmed()
    {
        await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()) with { FullName = " Juan Perez ", Email = " juan.perez@example.com " });

        Assert.Equal("Juan Perez", _seatA1.User!.FullName);
        Assert.Equal("juan.perez@example.com", _seatA1.User.Email);
    }

    [Fact]
    public async Task Parallel_purchases_of_the_same_seat_sell_it_once()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid())))));

        Assert.Single(results, r => r.Status == PurchaseTicketStatus.Purchased);
        Assert.Equal(9, results.Count(r => r.Status == PurchaseTicketStatus.AlreadySold));
    }

    private PurchaseTicketHandler CreateHandler(FakeEventRepository events, FakeTicketRepository tickets) =>
        new(new TicketPurchaseValidator(events), tickets, _users, _unitOfWork);

    private static PurchaseTicketCommand Command(Guid ticketId, Guid idempotencyKey) =>
        new(KnownEventId, ticketId, "Juan Perez", "juan.perez@example.com", idempotencyKey);
}
