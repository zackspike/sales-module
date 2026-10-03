using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Commands;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tests.Application;

public class PurchaseTicketHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Ticket _seatA1 = new(Guid.NewGuid(), KnownEventId, "A-1", DateTime.UtcNow);
    private readonly Ticket _seatA2 = new(Guid.NewGuid(), KnownEventId, "A-2", DateTime.UtcNow);
    private readonly PurchaseTicketHandler _handler;

    public PurchaseTicketHandlerTests()
    {
        var validator = new TicketPurchaseValidator(new FakeEventCatalog(KnownEventId));
        _handler = new PurchaseTicketHandler(validator, new FakeTicketRepository(_seatA1, _seatA2));
    }

    [Fact]
    public void Available_seat_is_purchased()
    {
        var result = _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.Purchased, result.Status);
        Assert.Same(_seatA1, result.Ticket);
        Assert.Equal(TicketStatus.Sold, _seatA1.Status);
        Assert.Equal("Juan Perez", _seatA1.FullName);
        Assert.Equal("juan.perez@example.com", _seatA1.Email);
    }

    [Fact]
    public void Retry_with_same_key_returns_original_purchase()
    {
        var key = Guid.NewGuid();
        var first = _handler.Handle(Command(_seatA1.Id, key));
        var code = first.Ticket!.TicketCode;

        var retry = _handler.Handle(Command(_seatA1.Id, key));

        Assert.Equal(PurchaseTicketStatus.Replayed, retry.Status);
        Assert.Same(_seatA1, retry.Ticket);
        Assert.Equal(code, retry.Ticket!.TicketCode);
    }

    [Fact]
    public void Sold_seat_with_new_key_is_already_sold()
    {
        _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()));

        var result = _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.AlreadySold, result.Status);
        Assert.Null(result.Ticket);
    }

    [Fact]
    public void Key_used_for_another_seat_is_a_conflict()
    {
        var key = Guid.NewGuid();
        _handler.Handle(Command(_seatA1.Id, key));

        var result = _handler.Handle(Command(_seatA2.Id, key));

        Assert.Equal(PurchaseTicketStatus.IdempotencyKeyConflict, result.Status);
        Assert.Null(result.Ticket);
    }

    [Fact]
    public void Empty_key_is_invalid()
    {
        var result = _handler.Handle(Command(_seatA1.Id, Guid.Empty));

        Assert.Equal(PurchaseTicketStatus.Invalid, result.Status);
        Assert.NotNull(result.Errors);
        Assert.True(result.Errors.ContainsKey("idempotencyKey"));
    }

    [Fact]
    public void Unknown_ticket_returns_not_found()
    {
        var result = _handler.Handle(Command(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.TicketNotFound, result.Status);
        Assert.Null(result.Ticket);
    }

    [Fact]
    public void Unknown_event_returns_event_not_found()
    {
        var command = new PurchaseTicketCommand(
            UnknownEventId,
            _seatA1.Id,
            "Juan Perez",
            "juan.perez@example.com",
            Guid.NewGuid());

        var result = _handler.Handle(command);

        Assert.Equal(PurchaseTicketStatus.EventNotFound, result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_name_is_invalid(string? fullName)
    {
        var command = Command(_seatA1.Id, Guid.NewGuid()) with { FullName = fullName };

        var result = _handler.Handle(command);

        Assert.Equal(PurchaseTicketStatus.Invalid, result.Status);
        Assert.True(result.Errors!.ContainsKey("fullName"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@missinguser.com")]
    public void Invalid_email_is_rejected(string? email)
    {
        var command = Command(_seatA1.Id, Guid.NewGuid()) with { Email = email };

        var result = _handler.Handle(command);

        Assert.Equal(PurchaseTicketStatus.Invalid, result.Status);
        Assert.True(result.Errors!.ContainsKey("email"));
    }

    [Fact]
    public void Customer_data_is_trimmed()
    {
        _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()) with { FullName = " Juan Perez ", Email = " juan.perez@example.com " });

        Assert.Equal("Juan Perez", _seatA1.FullName);
        Assert.Equal("juan.perez@example.com", _seatA1.Email);
    }

    [Fact]
    public void Parallel_purchases_of_the_same_seat_sell_it_once()
    {
        var results = new PurchaseTicketResult[10];

        Parallel.For(0, results.Length, index =>
        {
            results[index] = _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()));
        });

        Assert.Single(results, r => r.Status == PurchaseTicketStatus.Purchased);
        Assert.Equal(9, results.Count(r => r.Status == PurchaseTicketStatus.AlreadySold));
    }

    [Fact]
    public void Concurrent_retry_with_same_key_is_replayed_when_update_conflicts()
    {
        var key = Guid.NewGuid();
        var winner = SoldCopyOf(_seatA1, key);
        var handler = HandlerWith(new RacingTicketRepository(_seatA1, winner, new TicketAlreadySoldException(_seatA1.Id)));

        var result = handler.Handle(Command(_seatA1.Id, key));

        Assert.Equal(PurchaseTicketStatus.Replayed, result.Status);
        Assert.Same(winner, result.Ticket);
    }

    [Fact]
    public void Concurrent_purchase_with_another_key_is_already_sold_when_update_conflicts()
    {
        var winner = SoldCopyOf(_seatA1, Guid.NewGuid());
        var handler = HandlerWith(new RacingTicketRepository(_seatA1, winner, new TicketAlreadySoldException(_seatA1.Id)));

        var result = handler.Handle(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.AlreadySold, result.Status);
        Assert.Null(result.Ticket);
    }

    [Fact]
    public void Concurrent_reuse_of_key_for_another_seat_is_a_conflict_when_update_hits_unique_key()
    {
        var key = Guid.NewGuid();
        var winner = SoldCopyOf(_seatA1, key);
        var handler = HandlerWith(new RacingTicketRepository(_seatA2, winner, new DuplicateIdempotencyKeyException(key)));

        var result = handler.Handle(Command(_seatA2.Id, key));

        Assert.Equal(PurchaseTicketStatus.IdempotencyKeyConflict, result.Status);
        Assert.Null(result.Ticket);
    }

    [Fact]
    public void Seat_sold_with_same_key_after_idempotency_check_is_replayed()
    {
        var key = Guid.NewGuid();
        var winner = SoldCopyOf(_seatA1, key);
        // The loaded aggregate is already sold: the concurrent request committed between steps 1 and 2.
        var handler = HandlerWith(new RacingTicketRepository(winner, winner, new InvalidOperationException("Update must not be called.")));

        var result = handler.Handle(Command(_seatA1.Id, key));

        Assert.Equal(PurchaseTicketStatus.Replayed, result.Status);
        Assert.Same(winner, result.Ticket);
    }

    private static PurchaseTicketCommand Command(Guid ticketId, Guid idempotencyKey) =>
        new(KnownEventId, ticketId, "Juan Perez", "juan.perez@example.com", idempotencyKey);

    private static PurchaseTicketHandler HandlerWith(ITicketRepository tickets) =>
        new(new TicketPurchaseValidator(new FakeEventCatalog(KnownEventId)), tickets);

    private static Ticket SoldCopyOf(Ticket seat, Guid idempotencyKey)
    {
        var copy = new Ticket(seat.Id, seat.EventId, seat.SeatNumber, seat.CreatedAtUtc);
        copy.Purchase("Ana Lopez", "ana.lopez@example.com", idempotencyKey, DateTime.UtcNow);
        return copy;
    }

    private sealed class FakeEventCatalog(params Guid[] knownEvents) : IEventCatalog
    {
        private readonly HashSet<Guid> _events = [.. knownEvents];

        public bool Exists(Guid eventId) => _events.Contains(eventId);
    }

    private sealed class FakeTicketRepository(params Ticket[] tickets) : ITicketRepository
    {
        private readonly List<Ticket> _tickets = [.. tickets];
        private readonly Dictionary<Guid, Guid> _ticketIdsByIdempotencyKey = new();
        private readonly Lock _lock = new();

        public Ticket? GetById(Guid eventId, Guid ticketId)
        {
            lock (_lock)
            {
                return _tickets.FirstOrDefault(t => t.Id == ticketId && t.EventId == eventId);
            }
        }

        public Ticket? GetByIdempotencyKey(Guid idempotencyKey)
        {
            if (idempotencyKey == Guid.Empty)
            {
                return null;
            }

            lock (_lock)
            {
                return _ticketIdsByIdempotencyKey.TryGetValue(idempotencyKey, out var ticketId)
                    ? _tickets.FirstOrDefault(t => t.Id == ticketId)
                    : null;
            }
        }

        public void Update(Ticket ticket)
        {
            lock (_lock)
            {
                if (ticket.IdempotencyKey.HasValue && ticket.IdempotencyKey.Value != Guid.Empty)
                {
                    _ticketIdsByIdempotencyKey[ticket.IdempotencyKey.Value] = ticket.Id;
                }
            }
        }

        public void AddRange(Guid eventId, IEnumerable<Ticket> tickets) => throw new NotSupportedException();

        public IReadOnlyCollection<Ticket> GetByEvent(Guid eventId) => throw new NotSupportedException();
    }

    /// <summary>
    /// Simulates a request that loses a race: the handler's first idempotency lookup sees nothing,
    /// but every later lookup sees <c>winner</c>, committed meanwhile by a concurrent request.
    /// <see cref="Update"/> fails with the conflict the real repository would raise.
    /// </summary>
    private sealed class RacingTicketRepository(Ticket loaded, Ticket winner, Exception updateConflict) : ITicketRepository
    {
        private int _idempotencyLookups;

        public Ticket? GetById(Guid eventId, Guid ticketId) =>
            loaded.EventId == eventId && loaded.Id == ticketId ? loaded : null;

        public Ticket? GetByIdempotencyKey(Guid idempotencyKey) =>
            ++_idempotencyLookups > 1 && winner.IdempotencyKey == idempotencyKey ? winner : null;

        public void Update(Ticket ticket) => throw updateConflict;

        public void AddRange(Guid eventId, IEnumerable<Ticket> tickets) => throw new NotSupportedException();

        public IReadOnlyCollection<Ticket> GetByEvent(Guid eventId) => throw new NotSupportedException();
    }
}
