using BookingService.Domain;
using BookingService.Domain.Exceptions;

namespace BookingService.Application.Tests.Tickets;

public class TicketPurchaseTests
{
    private static readonly User Buyer = new() { Id = Guid.NewGuid(), FullName = "Juan Perez", Email = "juan.perez@example.com" };

    [Fact]
    public void New_ticket_is_available()
    {
        var ticket = new Ticket { Id = Guid.NewGuid() };

        Assert.Equal(TicketStatus.Available, ticket.Status);
        Assert.Null(ticket.PurchasedAtUtc);
        Assert.Null(ticket.User);
    }

    [Fact]
    public void Purchase_sells_the_seat_to_the_customer()
    {
        var ticket = new Ticket { Id = Guid.NewGuid() };
        var key = Guid.NewGuid();
        var purchasedAt = new DateTime(2026, 9, 26, 18, 30, 0, DateTimeKind.Utc);

        ticket.Purchase(Buyer, key, purchasedAt);

        Assert.Equal(TicketStatus.Sold, ticket.Status);
        Assert.Same(Buyer, ticket.User);
        Assert.Equal(Buyer.Id, ticket.UserId);
        Assert.Equal(key, ticket.IdempotencyKey);
        Assert.Equal(purchasedAt, ticket.PurchasedAtUtc);
        Assert.StartsWith("TK-", ticket.TicketCode);
    }

    [Fact]
    public void Sold_ticket_cannot_be_purchased_again()
    {
        var ticket = new Ticket { Id = Guid.NewGuid() };
        ticket.Purchase(Buyer, Guid.NewGuid(), DateTime.UtcNow);
        var originalCode = ticket.TicketCode;
        var otherBuyer = new User { Id = Guid.NewGuid(), FullName = "Ana Lopez", Email = "ana@example.com" };

        var exception = Assert.Throws<TicketAlreadySoldException>(() =>
            ticket.Purchase(otherBuyer, Guid.NewGuid(), DateTime.UtcNow));

        Assert.Equal("TICKET_ALREADY_SOLD", exception.Code);
        Assert.Equal(ticket.Id, exception.TicketId);
        Assert.Same(Buyer, ticket.User);
        Assert.Equal(originalCode, ticket.TicketCode);
    }

    [Fact]
    public void Purchase_requires_a_buyer()
    {
        var ticket = new Ticket { Id = Guid.NewGuid() };

        Assert.Throws<ArgumentNullException>(() => ticket.Purchase(null!, Guid.NewGuid(), DateTime.UtcNow));
        Assert.Equal(TicketStatus.Available, ticket.Status);
    }
}
