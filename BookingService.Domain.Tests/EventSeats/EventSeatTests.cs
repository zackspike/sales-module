using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;

namespace BookingService.Domain.Tests.EventSeats;

public class EventSeatTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VenueSeatId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime SoldAt = new(2026, 9, 26, 18, 30, 0, DateTimeKind.Utc);

    private static Purchase PurchaseHolding(EventSeat seat, string name = "Juan Perez", string email = "juan.perez@example.com")
    {
        var purchase = Purchase.Start(name, email, SoldAt, TimeSpan.FromMinutes(10));
        purchase.Hold(seat.EventId, [seat.Id], maxSeats: 3, SoldAt);
        return purchase;
    }

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
    public void Sell_marks_the_seat_as_sold_and_issues_a_ticket_to_the_purchase_buyer()
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);

        var ticket = seat.Sell(PurchaseHolding(seat), SoldAt);

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
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);

        var ticket = seat.Sell(PurchaseHolding(seat), SoldAt);

        Assert.StartsWith("TK-", ticket.TicketCode);
        Assert.True(Guid.TryParseExact(ticket.TicketCode[3..], "N", out _));
    }

    [Fact]
    public void Sold_seat_cannot_be_sold_again()
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
        seat.Sell(PurchaseHolding(seat), SoldAt);

        var exception = Assert.Throws<EventSeatAlreadySoldException>(() =>
            seat.Sell(PurchaseHolding(seat, "Ana Lopez", "ana@example.com"), SoldAt));

        Assert.Equal("EVENT_SEAT_ALREADY_SOLD", exception.Code);
        Assert.Equal(seat.Id, exception.EventSeatId);
        Assert.Equal(EventSeatStatus.Sold, seat.Status);
    }

    [Fact]
    public void Seat_cannot_be_sold_to_a_purchase_that_does_not_hold_it()
    {
        var seat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);
        var otherSeat = EventSeat.CreateAvailable(EventId, VenueSeatId, ZoneId);

        Assert.Throws<InvalidOperationException>(() => seat.Sell(PurchaseHolding(otherSeat), SoldAt));

        Assert.Equal(EventSeatStatus.Available, seat.Status);
    }
}
