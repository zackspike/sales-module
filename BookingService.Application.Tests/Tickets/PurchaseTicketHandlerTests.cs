using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Commands;
using BookingService.Domain.Tickets;
using BookingService.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Application.Tests.Tickets;

public class PurchaseTicketHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Ticket _seatA1 = new() { Id = Guid.NewGuid(), EventId = KnownEventId, SeatNumber = "A-1" };
    private readonly Ticket _seatA2 = new() { Id = Guid.NewGuid(), EventId = KnownEventId, SeatNumber = "A-2" };
    private readonly InMemorySeatLockStore _locks = new();
    private readonly FakeIdempotencyStore _idempotency = new();
    private readonly PurchaseTicketHandler _handler;

    public PurchaseTicketHandlerTests()
    {
        _handler = Handler(new FakeEventCatalog(KnownEventId), _seatA1, _seatA2);
        Reserve(_seatA1.Id);
        Reserve(_seatA2.Id);
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
        Assert.Equal(TicketStatus.Available, _seatA2.Status);
    }

    [Fact]
    public void Unknown_seat_is_not_found()
    {
        var result = _handler.Handle(Command(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.TicketNotFound, result.Status);
    }

    [Fact]
    public void Seat_of_another_event_is_not_found()
    {
        var otherEventId = Guid.NewGuid();
        var handler = Handler(new FakeEventCatalog(KnownEventId, otherEventId), _seatA1);

        var result = handler.Handle(Command(_seatA1.Id, Guid.NewGuid()) with { EventId = otherEventId });

        Assert.Equal(PurchaseTicketStatus.TicketNotFound, result.Status);
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
    }

    [Fact]
    public void Unknown_event_is_not_found()
    {
        var result = _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()) with { EventId = UnknownEventId });

        Assert.Equal(PurchaseTicketStatus.EventNotFound, result.Status);
    }

    [Fact]
    public void Invalid_customer_data_is_invalid_and_seat_stays_available()
    {
        var result = _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()) with { FullName = " ", Email = "abc" });

        Assert.Equal(PurchaseTicketStatus.Invalid, result.Status);
        Assert.Equal(["email", "fullName"], result.Errors.Keys.Order());
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
    }

    [Fact]
    public void Missing_idempotency_key_is_invalid()
    {
        var result = _handler.Handle(Command(_seatA1.Id, Guid.Empty));

        Assert.Equal(PurchaseTicketStatus.Invalid, result.Status);
        Assert.Equal(["idempotencyKey"], result.Errors.Keys);
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
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
    public void Seat_without_reservation_requires_one()
    {
        _locks.Release(_seatA1.Id, "juan.perez@example.com");

        var result = _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()));

        Assert.Equal(PurchaseTicketStatus.ReservationRequired, result.Status);
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
    }

    [Fact]
    public void Seat_reserved_by_another_buyer_requires_a_reservation()
    {
        var result = _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()) with { Email = "other@example.com" });

        Assert.Equal(PurchaseTicketStatus.ReservationRequired, result.Status);
        Assert.Equal(TicketStatus.Available, _seatA1.Status);
    }

    [Fact]
    public void Reservation_holder_email_is_case_insensitive()
    {
        var result = _handler.Handle(Command(_seatA1.Id, Guid.NewGuid()) with { Email = "Juan.Perez@Example.com" });

        Assert.Equal(PurchaseTicketStatus.Purchased, result.Status);
    }

    [Fact]
    public void Purchase_releases_the_seat_lock_and_remembers_the_key()
    {
        var key = Guid.NewGuid();

        _handler.Handle(Command(_seatA1.Id, key));

        Assert.Null(_locks.GetHolder(_seatA1.Id));
        Assert.Equal(new IdempotencyRecord(KnownEventId, _seatA1.Id), _idempotency.Get(key));
    }

    [Fact]
    public void Remembered_key_for_another_seat_is_a_conflict()
    {
        var key = Guid.NewGuid();
        _idempotency.Remember(key, new IdempotencyRecord(KnownEventId, _seatA1.Id), TimeSpan.FromMinutes(1));

        var result = _handler.Handle(Command(_seatA2.Id, key));

        Assert.Equal(PurchaseTicketStatus.IdempotencyKeyConflict, result.Status);
        Assert.Equal(TicketStatus.Available, _seatA2.Status);
    }

    private PurchaseTicketHandler Handler(FakeEventCatalog catalog, params Ticket[] tickets) =>
        new(
            new TicketPurchaseValidator(catalog),
            new FakeTicketRepository(tickets),
            _locks,
            _idempotency,
            new NoCache(),
            NullLogger<PurchaseTicketHandler>.Instance);

    private void Reserve(Guid ticketId) =>
        _locks.TryAcquire(ticketId, "juan.perez@example.com", TimeSpan.FromMinutes(10));

    private static PurchaseTicketCommand Command(Guid ticketId, Guid idempotencyKey) =>
        new(KnownEventId, ticketId, "Juan Perez", "juan.perez@example.com", idempotencyKey);

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, IdempotencyRecord> _records = new();

        public IdempotencyRecord? Get(Guid idempotencyKey) => _records.GetValueOrDefault(idempotencyKey);

        public void Remember(Guid idempotencyKey, IdempotencyRecord record, TimeSpan retention) =>
            _records[idempotencyKey] = record;
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

        public TicketPurchaseOutcome PurchaseOnce(Guid idempotencyKey, Guid eventId, Guid ticketId, Action<Ticket> purchase)
        {
            lock (_lock)
            {
                if (_ticketIdsByIdempotencyKey.TryGetValue(idempotencyKey, out var existingTicketId))
                {
                    return new TicketPurchaseOutcome(_tickets.First(t => t.Id == existingTicketId), Replayed: true);
                }

                var ticket = _tickets.FirstOrDefault(t => t.Id == ticketId && t.EventId == eventId);
                if (ticket is null)
                {
                    return new TicketPurchaseOutcome(null, Replayed: false);
                }

                purchase(ticket);
                _ticketIdsByIdempotencyKey[idempotencyKey] = ticket.Id;
                return new TicketPurchaseOutcome(ticket, Replayed: false);
            }
        }

        public void AddRange(Guid eventId, IEnumerable<Ticket> tickets) => throw new NotSupportedException();

        public IReadOnlyCollection<Ticket> GetByEvent(Guid eventId) => throw new NotSupportedException();

        public Ticket? GetById(Guid eventId, Guid ticketId) =>
            _tickets.FirstOrDefault(t => t.Id == ticketId && t.EventId == eventId);
    }
}
