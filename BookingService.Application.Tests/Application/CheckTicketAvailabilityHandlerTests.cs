using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Queries;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tests.Application;

public class CheckTicketAvailabilityHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly Ticket _availableSeat = new() { Id = Guid.NewGuid(), EventId = KnownEventId, SeatNumber = "A-1", Status = TicketStatus.Available };
    private readonly Ticket _soldSeat = new() { Id = Guid.NewGuid(), EventId = KnownEventId, SeatNumber = "A-2", Status = TicketStatus.Sold };

    private readonly CheckTicketAvailabilityHandler _handler;

    public CheckTicketAvailabilityHandlerTests()
    {
        var catalog = new FakeEventCatalog(KnownEventId);
        var repo = new FakeTicketRepository(_availableSeat, _soldSeat);
        _handler = new CheckTicketAvailabilityHandler(repo, catalog);
    }

    [Fact]
    public void Returns_availability_for_available_seat()
    {
        var result = _handler.Handle(new CheckTicketAvailabilityQuery(KnownEventId, _availableSeat.Id));

        Assert.NotNull(result);
        Assert.Equal(_availableSeat.Id, result.TicketId);
        Assert.Equal("A-1", result.SeatNumber);
        Assert.Equal("Available", result.Status);
    }

    [Fact]
    public void Returns_availability_for_sold_seat()
    {
        var result = _handler.Handle(new CheckTicketAvailabilityQuery(KnownEventId, _soldSeat.Id));

        Assert.NotNull(result);
        Assert.Equal(_soldSeat.Id, result.TicketId);
        Assert.Equal("A-2", result.SeatNumber);
        Assert.Equal("Sold", result.Status);
    }

    [Fact]
    public void Returns_null_when_seat_not_found()
    {
        var result = _handler.Handle(new CheckTicketAvailabilityQuery(KnownEventId, Guid.NewGuid()));

        Assert.Null(result);
    }

    [Fact]
    public void Returns_null_when_event_not_found()
    {
        var result = _handler.Handle(new CheckTicketAvailabilityQuery(UnknownEventId, _availableSeat.Id));

        Assert.Null(result);
    }

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

        public Ticket GetOrAdd(Guid idempotencyKey, Ticket ticket, out bool wasCreated) => throw new NotSupportedException();
        public void AddRange(Guid eventId, IEnumerable<Ticket> tickets) => throw new NotSupportedException();
        public IReadOnlyCollection<Ticket> GetByEvent(Guid eventId) => throw new NotSupportedException();
        public Ticket? GetById(Guid id) => throw new NotSupportedException();
        public IReadOnlyCollection<Ticket> GetAll() => throw new NotSupportedException();
        public Ticket Update(Ticket ticket) => throw new NotSupportedException();
        public bool Remove(Guid id) => throw new NotSupportedException();
        public TicketPurchaseOutcome PurchaseOnce(Guid idempotencyKey, Guid eventId, Guid ticketId, Action<Ticket> purchase) => throw new NotSupportedException();
    }
}
