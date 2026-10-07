using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Queries;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tests.Tickets;

public class GetAvailableTicketsHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly Ticket _seatA1 = new() { Id = Guid.NewGuid(), EventId = KnownEventId, SeatNumber = "A-1", Status = TicketStatus.Available };
    private readonly Ticket _seatA2 = new() { Id = Guid.NewGuid(), EventId = KnownEventId, SeatNumber = "A-2", Status = TicketStatus.Sold };
    private readonly Ticket _seatA3 = new() { Id = Guid.NewGuid(), EventId = KnownEventId, SeatNumber = "A-3", Status = TicketStatus.Available };

    private readonly GetAvailableTicketsHandler _handler;

    public GetAvailableTicketsHandlerTests()
    {
        var catalog = new FakeEventCatalog(KnownEventId);
        var repo = new FakeTicketRepository(_seatA1, _seatA2, _seatA3);
        _handler = new GetAvailableTicketsHandler(repo, catalog);
    }

    [Fact]
    public void Returns_only_available_tickets_for_known_event()
    {
        var result = _handler.Handle(new GetAvailableTicketsQuery(KnownEventId));

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(["A-1", "A-3"], result.Select(t => t.SeatNumber));
        Assert.All(result, t => Assert.Equal("Available", t.Status));
    }

    [Fact]
    public void Returns_null_when_event_does_not_exist()
    {
        var result = _handler.Handle(new GetAvailableTicketsQuery(UnknownEventId));

        Assert.Null(result);
    }

    [Fact]
    public void Returns_empty_list_when_all_seats_are_sold()
    {
        var eventId = Guid.NewGuid();
        var catalog = new FakeEventCatalog(eventId);
        var soldSeat = new Ticket { Id = Guid.NewGuid(), EventId = eventId, SeatNumber = "A-1", Status = TicketStatus.Sold };
        var handler = new GetAvailableTicketsHandler(new FakeTicketRepository(soldSeat), catalog);

        var result = handler.Handle(new GetAvailableTicketsQuery(eventId));

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    private sealed class FakeEventCatalog(params Guid[] knownEvents) : IEventCatalog
    {
        private readonly HashSet<Guid> _events = [.. knownEvents];
        public bool Exists(Guid eventId) => _events.Contains(eventId);
    }

    private sealed class FakeTicketRepository(params Ticket[] tickets) : ITicketRepository
    {
        private readonly List<Ticket> _tickets = [.. tickets];

        public IReadOnlyCollection<Ticket> GetByEvent(Guid eventId) =>
            _tickets.Where(t => t.EventId == eventId).ToList();
        public void AddRange(Guid eventId, IEnumerable<Ticket> tickets) => throw new NotSupportedException();
        public Ticket? GetById(Guid eventId, Guid ticketId) => throw new NotSupportedException();
        public TicketPurchaseOutcome PurchaseOnce(Guid idempotencyKey, Guid eventId, Guid ticketId, Action<Ticket> purchase) => throw new NotSupportedException();
    }
}
