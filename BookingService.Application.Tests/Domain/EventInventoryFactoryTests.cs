using BookingService.Domain.Events;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tests.Domain;

public class EventInventoryFactoryTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Creates_exact_number_of_available_tickets()
    {
        var tickets = EventInventoryFactory.CreateInitialInventory(EventId, 50);

        Assert.Equal(50, tickets.Count);
        Assert.All(tickets, t =>
        {
            Assert.Equal(EventId, t.EventId);
            Assert.Equal(TicketStatus.Available, t.Status);
            Assert.NotEqual(Guid.Empty, t.Id);
            Assert.Null(t.PurchasedAtUtc);
            Assert.Empty(t.TicketCode);
        });
    }

    [Fact]
    public void Creates_unique_ticket_ids_and_correct_seat_numbers()
    {
        var tickets = EventInventoryFactory.CreateInitialInventory(EventId, 5, "VIP");

        Assert.Equal(5, tickets.Select(t => t.Id).Distinct().Count());
        Assert.Equal(["VIP-1", "VIP-2", "VIP-3", "VIP-4", "VIP-5"], tickets.Select(t => t.SeatNumber));
    }

    [Fact]
    public void Overload_with_event_instance_reads_properties_correctly()
    {
        var @event = new Event
        {
            Id = EventId,
            Name = "Rock Fest 2026",
            TotalSeats = 10
        };

        var tickets = EventInventoryFactory.CreateInitialInventory(@event);

        Assert.Equal(10, tickets.Count);
        Assert.Equal("A-1", tickets[0].SeatNumber);
        Assert.Equal("A-10", tickets[9].SeatNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    public void Throws_when_total_seats_is_not_positive(int totalSeats)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EventInventoryFactory.CreateInitialInventory(EventId, totalSeats));
    }

    [Fact]
    public void Throws_when_event_id_is_empty()
    {
        Assert.Throws<ArgumentException>(() =>
            EventInventoryFactory.CreateInitialInventory(Guid.Empty, 10));
    }
}
