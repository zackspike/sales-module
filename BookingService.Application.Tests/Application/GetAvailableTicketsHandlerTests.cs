using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Queries;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tests.Application;

public class GetAvailableTicketsHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly Ticket _seatA1 = new(Guid.NewGuid(), KnownEventId, "A-1", DateTime.UtcNow);
    private readonly Ticket _seatA2 = new(Guid.NewGuid(), KnownEventId, "A-2", DateTime.UtcNow);
    private readonly Ticket _seatA3 = new(Guid.NewGuid(), KnownEventId, "A-3", DateTime.UtcNow);

    private readonly GetAvailableTicketsHandler _handler;

    public GetAvailableTicketsHandlerTests()
    {
        _seatA2.Purchase("Jane Doe", "jane@example.com", Guid.NewGuid(), DateTime.UtcNow);

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
        Assert.Contains(result, s => s.TicketId == _seatA1.Id && s.SeatNumber == "A-1" && s.Status == "Available");
        Assert.Contains(result, s => s.TicketId == _seatA3.Id && s.SeatNumber == "A-3" && s.Status == "Available");
        Assert.DoesNotContain(result, s => s.TicketId == _seatA2.Id);
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
        var allSoldEventId = Guid.NewGuid();
        var soldSeat = new Ticket(Guid.NewGuid(), allSoldEventId, "B-1", DateTime.UtcNow);
        soldSeat.Purchase("John Smith", "john@example.com", Guid.NewGuid(), DateTime.UtcNow);

        var catalog = new FakeEventCatalog(allSoldEventId);
        var repo = new FakeTicketRepository(soldSeat);
        var handler = new GetAvailableTicketsHandler(repo, catalog);

        var result = handler.Handle(new GetAvailableTicketsQuery(allSoldEventId));

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
        public Ticket? GetByIdempotencyKey(Guid idempotencyKey) => throw new NotSupportedException();
        public void Update(Ticket ticket) => throw new NotSupportedException();
    }
}
