using BookingService.Domain.Tickets;
using BookingService.Infrastructure.Persistence;

namespace BookingService.Application.Tests.Infrastructure;

public class EventSeedingIntegrationTests
{
    [Fact]
    public void Event_catalog_and_ticket_repository_seed_consistent_inventory()
    {
        // Arrange & Act (APP-03 / DOM-03)
        var catalog = new InMemoryEventCatalog();
        var repository = new InMemoryTicketRepository(seedDefaultInventory: true);

        var defaultEvent = InMemoryEventCatalog.DefaultEvent;

        // Assert: Event exists in catalog with contract specification (ALIGN-01)
        Assert.True(catalog.Exists(defaultEvent.Id));
        var fetchedEvent = catalog.GetById(defaultEvent.Id);
        Assert.NotNull(fetchedEvent);
        Assert.Equal("Rock Fest 2026", fetchedEvent.Name);
        Assert.Equal("The Rockers", fetchedEvent.Artist);
        Assert.Equal("Estadio Nacional", fetchedEvent.VenueName);
        Assert.Equal(50, fetchedEvent.TotalSeats);

        // Assert: Seats exist in repository matching total seats (DOM-03 / APP-03)
        var seats = repository.GetByEvent(defaultEvent.Id);
        Assert.Equal(fetchedEvent.TotalSeats, seats.Count);

        // Assert: All seats are initial Available inventory
        var orderedSeats = seats.OrderBy(s => int.Parse(s.SeatNumber.Split('-')[1])).ToList();
        for (var i = 0; i < 50; i++)
        {
            var seat = orderedSeats[i];
            Assert.Equal($"A-{i + 1}", seat.SeatNumber);
            Assert.Equal(TicketStatus.Available, seat.Status);
            Assert.Equal(defaultEvent.Id, seat.EventId);
            Assert.NotEqual(Guid.Empty, seat.Id);
            Assert.NotEqual(default, seat.CreatedAtUtc);
            Assert.Null(seat.PurchasedAtUtc);
            Assert.Null(seat.TicketCode);
            Assert.Null(seat.FullName);
            Assert.Null(seat.Email);
            Assert.Null(seat.IdempotencyKey);
        }
    }
}
