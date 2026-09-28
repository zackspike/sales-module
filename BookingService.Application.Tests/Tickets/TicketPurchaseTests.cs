using BookingService.Domain;
using BookingService.Domain.Exceptions;

namespace BookingService.Application.Tests.Tickets;

public class TicketPurchaseTests
{
    [Fact]
    public void New_ticket_is_available()
    {
        var ticket = new Ticket { Id = Guid.NewGuid(), SeatNumber = "A-1" };

        Assert.Equal(TicketStatus.Available, ticket.Status);
        Assert.Null(ticket.PurchasedAtUtc);
    }

    [Fact]
    public void Purchase_sells_the_seat_to_the_customer()
    {
        var ticket = new Ticket { Id = Guid.NewGuid(), SeatNumber = "A-1" };
        var key = Guid.NewGuid();
        var purchasedAt = new DateTime(2026, 9, 26, 18, 30, 0, DateTimeKind.Utc);

        ticket.Purchase("Juan Perez", "juan.perez@example.com", key, purchasedAt);

        Assert.Equal(TicketStatus.Sold, ticket.Status);
        Assert.Equal("Juan Perez", ticket.FullName);
        Assert.Equal("juan.perez@example.com", ticket.Email);
        Assert.Equal(key, ticket.IdempotencyKey);
        Assert.Equal(purchasedAt, ticket.PurchasedAtUtc);
        Assert.StartsWith("TK-", ticket.TicketCode);
    }

    [Fact]
    public void Sold_ticket_cannot_be_purchased_again()
    {
        var ticket = new Ticket { Id = Guid.NewGuid(), SeatNumber = "A-1" };
        ticket.Purchase("Juan Perez", "juan.perez@example.com", Guid.NewGuid(), DateTime.UtcNow);
        var originalCode = ticket.TicketCode;

        var exception = Assert.Throws<TicketAlreadySoldException>(() =>
            ticket.Purchase("Ana Lopez", "ana@example.com", Guid.NewGuid(), DateTime.UtcNow));

        Assert.Equal("TICKET_ALREADY_SOLD", exception.Code);
        Assert.Equal(ticket.Id, exception.TicketId);
        Assert.Equal("Juan Perez", ticket.FullName);
        Assert.Equal(originalCode, ticket.TicketCode);
    }
}
