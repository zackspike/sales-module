using BookingService.Domain.EventSeats;

namespace BookingService.Domain.Tests.EventSeats;

public class EventSeatTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VenueSeatId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime SoldAt = new(2026, 9, 26, 18, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateAvailable_builds_an_available_seat()
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);

        Assert.NotEqual(Guid.Empty, seat.Id);
        Assert.Equal(EventId, seat.EventId);
        Assert.Equal(VenueSeatId, seat.VenueSeatId);
        Assert.Equal(ZoneId, seat.ZoneId);
        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }

    [Fact]
    public void Sell_marks_the_seat_as_sold_and_issues_its_ticket()
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);

        var ticket = seat.Sell("Juan Perez", "juan.perez@example.com", SoldAt);

        Assert.Equal(EventSeatStatus.Sold, seat.Status);
        Assert.NotEqual(Guid.Empty, ticket.Id);
        Assert.Equal(seat.Id, ticket.EventSeatId);
        Assert.Equal("Juan Perez", ticket.HolderName);
        Assert.Equal("juan.perez@example.com", ticket.HolderEmail);
        Assert.Equal(SoldAt, ticket.SoldAtUtc);
    }

    [Fact]
    public void Sold_ticket_has_a_valid_ticket_code()
    {
        var ticket = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId)
            .Sell("Juan Perez", "juan.perez@example.com", SoldAt);

        Assert.StartsWith("TK-", ticket.TicketCode);
        Assert.True(Guid.TryParseExact(ticket.TicketCode[3..], "N", out _));
    }

    [Fact]
    public void Sold_seat_cannot_be_sold_again()
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
        seat.Sell("Juan Perez", "juan.perez@example.com", SoldAt);

        var exception = Assert.Throws<EventSeatAlreadySoldException>(() =>
            seat.Sell("Ana Lopez", "ana@example.com", SoldAt));

        Assert.Equal("EVENT_SEAT_ALREADY_SOLD", exception.Code);
        Assert.Equal(seat.Id, exception.EventSeatId);
        Assert.Equal(EventSeatStatus.Sold, seat.Status);
    }
}
