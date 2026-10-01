using System.Collections.Concurrent;
using BookingService.Application.Tickets;
using BookingService.Domain;

namespace BookingService.Infrastructure;

/// <summary>
/// In-memory mock catalog of known events (SP-03 / APP-03).
/// Pre-seeded with the agreed test event from contract negotiations.
/// </summary>
public class InMemoryEventCatalog : IEventCatalog
{
    private readonly ConcurrentDictionary<Guid, Event> _events = new();

    public static readonly Event DefaultEvent = new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Name = "Rock Fest 2026",
        Artist = "The Rockers",
        VenueId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        VenueName = "Estadio Nacional",
        Date = DateTime.Parse("2026-11-20T20:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal),
        TotalSeats = 50
    };

    public InMemoryEventCatalog()
    {
        _events.TryAdd(DefaultEvent.Id, DefaultEvent);
    }

    public InMemoryEventCatalog(IEnumerable<Event> events)
    {
        foreach (var @event in events)
        {
            _events.TryAdd(@event.Id, @event);
        }
    }

    public bool Exists(Guid eventId) => _events.ContainsKey(eventId);

    public Event? GetById(Guid eventId) => _events.TryGetValue(eventId, out var @event) ? @event : null;

    public IReadOnlyCollection<Event> GetAll() => _events.Values.ToList();

    public void Add(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _events[@event.Id] = @event;
    }
}
