using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Commands;
using BookingService.Domain.Tickets;
using BookingService.Infrastructure.Caching;

namespace BookingService.Application.Tests.Tickets;

public class ReserveSeatHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Ticket _availableSeat = new() { Id = Guid.NewGuid(), EventId = KnownEventId, SeatNumber = "A-1" };
    private readonly Ticket _soldSeat = new() { Id = Guid.NewGuid(), EventId = KnownEventId, SeatNumber = "A-2", Status = TicketStatus.Sold };
    private readonly InMemorySeatLockStore _locks = new();
    private readonly ReserveSeatHandler _handler;

    public ReserveSeatHandlerTests()
    {
        var validator = new TicketPurchaseValidator(new FakeEventCatalog(KnownEventId));
        _handler = new ReserveSeatHandler(validator, new FakeTicketRepository(_availableSeat, _soldSeat), _locks);
    }

    [Fact]
    public void Available_seat_is_locked_for_the_buyer()
    {
        var result = _handler.Handle(Command(_availableSeat.Id));

        Assert.Equal(ReserveSeatStatus.Reserved, result.Status);
        Assert.Same(_availableSeat, result.Ticket);
        Assert.Equal("juan.perez@example.com", _locks.GetHolder(_availableSeat.Id));
        Assert.True(result.ExpiresAtUtc > DateTime.UtcNow + SeatReservationPolicy.LockDuration - TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Same_buyer_renews_the_reservation()
    {
        _handler.Handle(Command(_availableSeat.Id));

        var result = _handler.Handle(Command(_availableSeat.Id) with { Email = " JUAN.perez@example.com " });

        Assert.Equal(ReserveSeatStatus.Reserved, result.Status);
    }

    [Fact]
    public void Seat_reserved_by_another_buyer_is_locked()
    {
        _handler.Handle(Command(_availableSeat.Id));

        var result = _handler.Handle(Command(_availableSeat.Id) with { Email = "other@example.com" });

        Assert.Equal(ReserveSeatStatus.LockedByAnotherBuyer, result.Status);
        Assert.Equal("juan.perez@example.com", _locks.GetHolder(_availableSeat.Id));
    }

    [Fact]
    public void Sold_seat_is_not_reserved()
    {
        var result = _handler.Handle(Command(_soldSeat.Id));

        Assert.Equal(ReserveSeatStatus.AlreadySold, result.Status);
        Assert.Null(_locks.GetHolder(_soldSeat.Id));
    }

    [Fact]
    public void Unknown_seat_is_not_found()
    {
        var result = _handler.Handle(Command(Guid.NewGuid()));

        Assert.Equal(ReserveSeatStatus.TicketNotFound, result.Status);
    }

    [Fact]
    public void Unknown_event_is_not_found()
    {
        var result = _handler.Handle(Command(_availableSeat.Id) with { EventId = Guid.NewGuid() });

        Assert.Equal(ReserveSeatStatus.EventNotFound, result.Status);
    }

    [Fact]
    public void Invalid_buyer_data_is_invalid_and_seat_stays_unlocked()
    {
        var result = _handler.Handle(Command(_availableSeat.Id) with { FullName = " ", Email = "abc" });

        Assert.Equal(ReserveSeatStatus.Invalid, result.Status);
        Assert.Equal(["email", "fullName"], result.Errors.Keys.Order());
        Assert.Null(_locks.GetHolder(_availableSeat.Id));
    }

    [Fact]
    public void Parallel_buyers_of_the_same_seat_reserve_it_once()
    {
        var results = new ReserveSeatResult[10];

        Parallel.For(0, results.Length, index =>
        {
            results[index] = _handler.Handle(Command(_availableSeat.Id) with { Email = $"buyer{index}@example.com" });
        });

        Assert.Single(results, r => r.Status == ReserveSeatStatus.Reserved);
        Assert.Equal(9, results.Count(r => r.Status == ReserveSeatStatus.LockedByAnotherBuyer));
    }

    private static ReserveSeatCommand Command(Guid ticketId) =>
        new(KnownEventId, ticketId, "Juan Perez", "juan.perez@example.com");

    private sealed class FakeEventCatalog(params Guid[] knownEvents) : IEventCatalog
    {
        private readonly HashSet<Guid> _events = [.. knownEvents];

        public bool Exists(Guid eventId) => _events.Contains(eventId);
    }

    private sealed class FakeTicketRepository(params Ticket[] tickets) : ITicketRepository
    {
        private readonly List<Ticket> _tickets = [.. tickets];

        public Ticket? GetById(Guid eventId, Guid ticketId) =>
            _tickets.FirstOrDefault(t => t.EventId == eventId && t.Id == ticketId);

        public void AddRange(Guid eventId, IEnumerable<Ticket> tickets) => throw new NotSupportedException();

        public IReadOnlyCollection<Ticket> GetByEvent(Guid eventId) => throw new NotSupportedException();

        public TicketPurchaseOutcome PurchaseOnce(Guid idempotencyKey, Guid eventId, Guid ticketId, Action<Ticket> purchase) =>
            throw new NotSupportedException();
    }
}
