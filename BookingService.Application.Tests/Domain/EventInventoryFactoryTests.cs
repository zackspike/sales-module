using BookingService.Domain;

namespace BookingService.Application.Tests.Domain;

public class EventInventoryFactoryTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Zone _zone = new() { Id = Guid.NewGuid(), EventId = EventId, Price = 25m };

    [Fact]
    public void Creates_exact_number_of_available_tickets()
    {
        var tickets = EventInventoryFactory.CreateInitialInventory(_zone, 50);

        Assert.Equal(50, tickets.Count);
        Assert.All(tickets, t =>
        {
            Assert.Equal(TicketStatus.Available, t.Status);
            Assert.NotEqual(Guid.Empty, t.Id);
            Assert.Equal(t.Seat!.Id, t.SeatId);
            Assert.Same(_zone, t.Seat.Zone);
            Assert.Equal(_zone.Id, t.Seat.ZoneId);
            Assert.Null(t.PurchasedAtUtc);
            Assert.Null(t.TicketCode);
            Assert.Null(t.User);
        });
    }

    [Fact]
    public void Creates_unique_ticket_and_seat_ids_and_correct_seat_numbers()
    {
        var tickets = EventInventoryFactory.CreateInitialInventory(_zone, 5, "VIP");

        Assert.Equal(5, tickets.Select(t => t.Id).Distinct().Count());
        Assert.Equal(5, tickets.Select(t => t.SeatId).Distinct().Count());
        Assert.Equal(["VIP-1", "VIP-2", "VIP-3", "VIP-4", "VIP-5"], tickets.Select(t => t.Seat!.SeatNumber));
    }

    [Fact]
    public void Default_row_prefix_is_A()
    {
        var tickets = EventInventoryFactory.CreateInitialInventory(_zone, 10);

        Assert.Equal("A-1", tickets[0].Seat!.SeatNumber);
        Assert.Equal("A-10", tickets[9].Seat!.SeatNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    public void Throws_when_total_seats_is_not_positive(int totalSeats)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EventInventoryFactory.CreateInitialInventory(_zone, totalSeats));
    }

    [Fact]
    public void Throws_when_event_id_is_empty()
    {
        var zone = new Zone { Id = Guid.NewGuid(), EventId = Guid.Empty };

        Assert.Throws<ArgumentException>(() => EventInventoryFactory.CreateInitialInventory(zone, 10));
    }

    [Fact]
    public void Throws_when_zone_id_is_empty()
    {
        var zone = new Zone { Id = Guid.Empty, EventId = EventId };

        Assert.Throws<ArgumentException>(() => EventInventoryFactory.CreateInitialInventory(zone, 10));
    }
}
