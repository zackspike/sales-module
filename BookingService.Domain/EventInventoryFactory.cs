namespace BookingService.Domain;

/// <summary>
/// Domain factory that generates event seats for a given event based on the venue seats.
/// EventId is a parameter obtained from the EventService, and VenueSeats are obtained from the VenueService.
/// The factory creates a list of EventSeat entities that represent the available seats for the event, each associated with a specific venue seat.
/// </summary>
public static class EventInventoryFactory
{
    public static List<EventSeat> GenerateEventSeats(Guid eventId, IEnumerable<VenueSeat> venueSeats)
    {
        var eventSeats = new List<EventSeat>();

        foreach (var venueSeat in venueSeats)
        {
            var eventSeat = EventSeat.CreateAvailable(eventId, venueSeat.Id);
            eventSeats.Add(eventSeat);
        }

        return eventSeats;
    }
}
