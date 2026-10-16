using BookingService.Domain.EventSeats;
using BookingService.Domain.Venues;

namespace BookingService.Domain.Tests.EventSeats;

public class EventInventoryFactoryTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Generates_one_available_event_seat_per_venue_seat()
    {
        var zoneA = Guid.NewGuid();
        var zoneB = Guid.NewGuid();
        VenueSeat[] venueSeats =
        [
            new(Guid.NewGuid(), zoneA, "A-1"),
            new(Guid.NewGuid(), zoneA, "A-2"),
            new(Guid.NewGuid(), zoneB, "B-1")
        ];

        var eventSeats = EventInventoryFactory.GenerateEventSeats(EventId, venueSeats);

        Assert.Equal(venueSeats.Length, eventSeats.Count);
        Assert.All(eventSeats.Zip(venueSeats), pair =>
        {
            var (eventSeat, venueSeat) = pair;
            Assert.Equal(EventId, eventSeat.EventId);
            Assert.Equal(venueSeat.Id, eventSeat.VenueSeatId);
            Assert.Equal(venueSeat.ZoneId, eventSeat.ZoneId);
            Assert.Equal(EventSeatStatus.Available, eventSeat.Status);
        });
    }

    [Fact]
    public void Generated_event_seats_have_unique_ids()
    {
        var venueSeats = Enumerable.Range(1, 100)
            .Select(n => new VenueSeat(Guid.NewGuid(), Guid.NewGuid(), $"A-{n}"));

        var eventSeats = EventInventoryFactory.GenerateEventSeats(EventId, venueSeats);

        Assert.Equal(100, eventSeats.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Generates_no_event_seats_for_a_venue_without_seats()
    {
        var eventSeats = EventInventoryFactory.GenerateEventSeats(EventId, []);

        Assert.Empty(eventSeats);
    }
}
