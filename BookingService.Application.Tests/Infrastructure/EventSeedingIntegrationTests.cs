using BookingService.Domain.EventSeats;
using BookingService.Infrastructure.Persistence;

namespace BookingService.Application.Tests.Infrastructure;

public class EventSeedingIntegrationTests
{
    [Fact]
    public void Event_catalog_and_seat_repository_seed_consistent_inventory()
    {
        // Arrange & Act (APP-03 / DOM-03)
        var catalog = new InMemoryEventCatalog();
        var repository = new InMemoryEventSeatRepository(seedDefaultInventory: true);

        var defaultEvent = InMemoryEventCatalog.DefaultEvent;

        // Assert: Event exists in catalog with contract specification (ALIGN-01)
        Assert.True(catalog.Exists(defaultEvent.Id));
        var fetchedEvent = catalog.GetById(defaultEvent.Id);
        Assert.NotNull(fetchedEvent);
        Assert.Equal("Rock Fest 2026", fetchedEvent.Name);
        Assert.Equal("The Rockers", fetchedEvent.ArtistName);
        Assert.Equal("Estadio Nacional", fetchedEvent.VenueName);

        // Assert: Venue seats match the default inventory legend (A-1..A-50)
        var venueSeats = InMemoryVenueSeatCatalog.DefaultVenueSeats;
        Assert.Equal(50, venueSeats.Count);
        Assert.Equal("A-1", venueSeats[0].SeatNumber);
        Assert.Equal("A-50", venueSeats[^1].SeatNumber);
        Assert.All(venueSeats, vs => Assert.Equal(InMemoryVenueSeatCatalog.DefaultZoneId, vs.ZoneId));

        // Assert: Event seats exist in repository matching the venue seats (DOM-03 / APP-03)
        var seats = repository.GetByEvent(defaultEvent.Id);
        Assert.Equal(venueSeats.Count, seats.Count);
        Assert.Equal(venueSeats.Select(vs => vs.Id).Order(), seats.Select(s => s.VenueSeatId).Order());

        // Assert: All seats are initial Available inventory
        Assert.All(seats, s =>
        {
            Assert.Equal(EventSeatStatus.Available, s.Status);
            Assert.Equal(defaultEvent.Id, s.EventId);
            Assert.Equal(InMemoryVenueSeatCatalog.DefaultZoneId, s.ZoneId);
            Assert.NotEqual(Guid.Empty, s.Id);
        });
    }
}
