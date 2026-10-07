using BookingService.Application.Tests.TestDoubles;
using BookingService.Application.Tickets.Queries;
using BookingService.Domain;

namespace BookingService.Application.Tests.Tickets;

public class CheckTicketAvailabilityHandlerTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly Ticket _availableSeat = TestTickets.Create(KnownEventId, "A-1");
    private readonly Ticket _soldSeat = TestTickets.Create(KnownEventId, "A-2", TicketStatus.Sold);

    private readonly CheckTicketAvailabilityHandler _handler;

    public CheckTicketAvailabilityHandlerTests()
    {
        var events = new FakeEventRepository(KnownEventId);
        var tickets = new FakeTicketRepository(_availableSeat, _soldSeat);
        _handler = new CheckTicketAvailabilityHandler(tickets, events);
    }

    [Fact]
    public async Task Returns_availability_for_available_seat()
    {
        var result = await _handler.HandleAsync(new CheckTicketAvailabilityQuery(KnownEventId, _availableSeat.Id));

        Assert.NotNull(result);
        Assert.Equal(_availableSeat.Id, result.TicketId);
        Assert.Equal("A-1", result.SeatNumber);
        Assert.Equal("Available", result.Status);
    }

    [Fact]
    public async Task Returns_availability_for_sold_seat()
    {
        var result = await _handler.HandleAsync(new CheckTicketAvailabilityQuery(KnownEventId, _soldSeat.Id));

        Assert.NotNull(result);
        Assert.Equal(_soldSeat.Id, result.TicketId);
        Assert.Equal("A-2", result.SeatNumber);
        Assert.Equal("Sold", result.Status);
    }

    [Fact]
    public async Task Returns_null_when_seat_not_found()
    {
        var result = await _handler.HandleAsync(new CheckTicketAvailabilityQuery(KnownEventId, Guid.NewGuid()));

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_when_event_not_found()
    {
        var result = await _handler.HandleAsync(new CheckTicketAvailabilityQuery(UnknownEventId, _availableSeat.Id));

        Assert.Null(result);
    }
}
