using BookingService.Application.EventSeats.Queries;
using BookingService.Application.Tests.EventSeats.Fakes;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Venues;

namespace BookingService.Application.Tests.EventSeats.Queries;

public class GetTicketByCodeHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VenueSeatId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Returns_the_ticket_with_its_seat_number()
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
        var repository = new FakeEventSeatRepository(seat);
        var code = repository.RememberSale(Guid.NewGuid(), seat).Ticket.TicketCode;

        var ticket = await Handler(repository).HandleAsync(new($" {code} "), CancellationToken.None);

        Assert.NotNull(ticket);
        Assert.Equal(seat.Id, ticket.EventSeatId);
        Assert.Equal(EventId, ticket.EventId);
        Assert.Equal("A-1", ticket.SeatNumber);
        Assert.Equal("original", ticket.HolderName);
        Assert.Equal(code, ticket.TicketCode);
    }

    [Fact]
    public async Task Unknown_code_returns_null()
    {
        var ticket = await Handler(new FakeEventSeatRepository()).HandleAsync(new("TK-unknown"), CancellationToken.None);

        Assert.Null(ticket);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_code_returns_null(string code)
    {
        var ticket = await Handler(new FakeEventSeatRepository()).HandleAsync(new(code), CancellationToken.None);

        Assert.Null(ticket);
    }

    private static GetTicketByCodeHandler Handler(FakeEventSeatRepository repository) =>
        new(repository, new FakeVenueSeatCatalog(new VenueSeat(VenueSeatId, ZoneId, "A-1")));
}
