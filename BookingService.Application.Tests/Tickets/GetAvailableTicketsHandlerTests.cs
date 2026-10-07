using BookingService.Application.Tests.TestDoubles;
using BookingService.Application.Tickets.Queries;
using BookingService.Domain;

namespace BookingService.Application.Tests.Tickets;

public class GetAvailableTicketsHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly Ticket _seatA1 = TestTickets.Create(KnownEventId, "A-1");
    private readonly Ticket _seatA2 = TestTickets.Create(KnownEventId, "A-2", TicketStatus.Sold);
    private readonly Ticket _seatA3 = TestTickets.Create(KnownEventId, "A-3");

    private readonly GetAvailableTicketsHandler _handler;

    public GetAvailableTicketsHandlerTests()
    {
        var events = new FakeEventRepository(KnownEventId);
        var tickets = new FakeTicketRepository(_seatA3, _seatA1, _seatA2);
        _handler = new GetAvailableTicketsHandler(tickets, events);
    }

    [Fact]
    public async Task Returns_only_available_tickets_for_known_event_ordered_by_seat()
    {
        var result = await _handler.HandleAsync(new GetAvailableTicketsQuery(KnownEventId));

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(["A-1", "A-3"], result.Select(t => t.SeatNumber));
        Assert.All(result, t => Assert.Equal("Available", t.Status));
    }

    [Fact]
    public async Task Returns_null_when_event_does_not_exist()
    {
        var result = await _handler.HandleAsync(new GetAvailableTicketsQuery(UnknownEventId));

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_empty_list_when_all_seats_are_sold()
    {
        var eventId = Guid.NewGuid();
        var soldSeat = TestTickets.Create(eventId, "A-1", TicketStatus.Sold);
        var handler = new GetAvailableTicketsHandler(new FakeTicketRepository(soldSeat), new FakeEventRepository(eventId));

        var result = await handler.HandleAsync(new GetAvailableTicketsQuery(eventId));

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
