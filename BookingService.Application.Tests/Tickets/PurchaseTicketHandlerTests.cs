using BookingService.Application.Caching;
using BookingService.Application.Tests.TestDoubles;
using BookingService.Application.Tickets;
using BookingService.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Application.Tests.Tickets;

public class PurchaseTicketHandlerTests
{
    private const string BuyerEmail = "juan.perez@example.com";

    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Ticket _seatA1 = TestTickets.Create(KnownEventId, "A-1");
    private readonly Ticket _seatA2 = TestTickets.Create(KnownEventId, "A-2");
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeSeatLockStore _seatLocks = new();
    private readonly FakeIdempotencyStore _idempotency = new();
    private readonly FakeAvailableSeatsCache _availableSeats = new();
    private readonly PurchaseTicketHandler _handler;

    public PurchaseTicketHandlerTests()
    {
        _handler = CreateHandler(new FakeEventRepository(KnownEventId), new FakeTicketRepository(_seatA1, _seatA2));
    }

    [Fact]
    public async Task Reserved_seat_is_purchased()
    {
        var buyer = Reserve(_seatA1);

        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.Purchased, result.Status);
        Assert.Same(_seatA1, result.Ticket);
        Assert.Equal(TicketStatus.Sold, _seatA1.Status);
        Assert.Same(buyer, _seatA1.User);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Purchase_releases_the_lock_remembers_the_key_and_invalidates_the_cache()
    {
        Reserve(_seatA1);
        var key = Guid.NewGuid();

        await _handler.HandleAsync(Command(_seatA1.Id, key));

        Assert.Null(await _seatLocks.GetHolderAsync(_seatA1.Id));
        Assert.Equal(new IdempotencyRecord(KnownEventId, _seatA1.Id), await _idempotency.GetAsync(key));
        Assert.Equal([KnownEventId], _availableSeats.Invalidations);
    }

    [Fact]
    public async Task Purchase_without_reservation_is_forbidden()
    {
        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.ReservationRequired, result.Status);
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
        Assert.Equal(0, _unitOfWork.Transactions);
    }

    [Fact]
    public async Task Purchase_of_a_seat_reserved_by_another_buyer_is_forbidden()
    {
        Reserve(_seatA1, "ana.lopez@example.com");
        RegisterBuyer(BuyerEmail);

        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.ReservationRequired, result.Status);
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
    }

    [Fact]
    public async Task Purchase_after_the_reservation_expired_is_forbidden()
    {
        Reserve(_seatA1);
        _seatLocks.Expire(_seatA1.Id);

        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.ReservationRequired, result.Status);
    }

    [Fact]
    public async Task Retry_with_same_key_is_answered_from_the_idempotency_store()
    {
        Reserve(_seatA1);
        var key = Guid.NewGuid();
        var first = await _handler.HandleAsync(Command(_seatA1.Id, key));
        var transactionsAfterPurchase = _unitOfWork.Transactions;

        var retry = await _handler.HandleAsync(Command(_seatA1.Id, key));

        Assert.Equal(PurchaseTicketStatus.Replayed, retry.Status);
        Assert.Same(_seatA1, retry.Ticket);
        Assert.Equal(first.Ticket!.TicketCode, retry.Ticket!.TicketCode);
        Assert.Equal(transactionsAfterPurchase, _unitOfWork.Transactions);
    }

    [Fact]
    public async Task Retry_after_the_idempotency_entry_expired_is_still_a_replay()
    {
        Reserve(_seatA1);
        var key = Guid.NewGuid();
        await _handler.HandleAsync(Command(_seatA1.Id, key));
        _idempotency.Forget(key);

        var retry = await _handler.HandleAsync(Command(_seatA1.Id, key));

        Assert.Equal(PurchaseTicketStatus.Replayed, retry.Status);
    }

    [Fact]
    public async Task Sold_seat_with_new_key_is_already_sold()
    {
        Reserve(_seatA1);
        await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.AlreadySold, result.Status);
        Assert.Null(result.Ticket);
    }

    [Fact]
    public async Task Key_used_for_another_seat_is_a_conflict()
    {
        Reserve(_seatA1);
        Reserve(_seatA2);
        var key = Guid.NewGuid();
        await _handler.HandleAsync(Command(_seatA1.Id, key));

        var result = await _handler.HandleAsync(Command(_seatA2.Id, key));

        Assert.Equal(PurchaseTicketStatus.IdempotencyKeyConflict, result.Status);
        Assert.Equal(TicketStatus.Available, _seatA2.Status);
    }

    [Fact]
    public async Task Key_used_for_another_seat_is_a_conflict_after_the_idempotency_entry_expired()
    {
        Reserve(_seatA1);
        var key = Guid.NewGuid();
        await _handler.HandleAsync(Command(_seatA1.Id, key));
        _idempotency.Forget(key);

        var result = await _handler.HandleAsync(Command(_seatA2.Id, key));

        Assert.Equal(PurchaseTicketStatus.IdempotencyKeyConflict, result.Status);
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
        Reserve(_seatA1);

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
    public async Task Customer_email_is_trimmed_to_find_the_reservation()
    {
        Reserve(_seatA1);

        var result = await _handler.HandleAsync(
            Command(_seatA1.Id, Guid.NewGuid()) with { FullName = " Juan Perez ", Email = $" {BuyerEmail} " });

        Assert.Equal(PurchaseTicketStatus.Purchased, result.Status);
        Assert.Equal(BuyerEmail, _seatA1.User!.Email);
    }

    [Fact]
    public async Task Parallel_purchases_of_the_same_seat_sell_it_once()
    {
        Reserve(_seatA1);

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid())))));

        Assert.Single(results, r => r.Status == PurchaseTicketStatus.Purchased);
        Assert.Equal(9, results.Count(r => r.Status == PurchaseTicketStatus.AlreadySold));
    }

    [Fact]
    public async Task Cache_outage_after_commit_does_not_fail_the_purchase()
    {
        Reserve(_seatA1);
        _idempotency.ThrowOnRemember = true;

        var result = await _handler.HandleAsync(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.Purchased, result.Status);
        Assert.Equal(TicketStatus.Sold, _seatA1.Status);
    }

    private PurchaseTicketHandler CreateHandler(FakeEventRepository events, FakeTicketRepository tickets) =>
        new(
            new TicketPurchaseValidator(events),
            tickets,
            _users,
            _unitOfWork,
            _seatLocks,
            _idempotency,
            _availableSeats,
            NullLogger<PurchaseTicketHandler>.Instance);

    private User Reserve(Ticket ticket, string email = BuyerEmail)
    {
        var buyer = RegisterBuyer(email);
        _seatLocks.TryAcquireAsync(ticket.Id, buyer.Id, SeatReservationPolicy.LockDuration).GetAwaiter().GetResult();
        return buyer;
    }

    private User RegisterBuyer(string email)
    {
        var buyer = _users.Users.FirstOrDefault(u => u.Email == email);
        if (buyer is null)
        {
            buyer = new User { Id = Guid.NewGuid(), FullName = "Juan Perez", Email = email };
            _users.Add(buyer);
        }

        return buyer;
    }

    private static PurchaseTicketCommand Command(Guid ticketId, Guid idempotencyKey) =>
        new(KnownEventId, ticketId, "Juan Perez", BuyerEmail, idempotencyKey);
}
