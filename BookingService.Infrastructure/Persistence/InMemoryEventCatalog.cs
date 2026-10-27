using System.Collections.Concurrent;
using BookingService.Application.Abstractions;
using BookingService.Domain.Events;

namespace BookingService.Infrastructure.Persistence;

/// <summary>
/// In-memory mock catalog of known events (SP-03 / APP-03).
/// Pre-seeded with the agreed test event from contract negotiations.
/// </summary>
public class InMemoryEventCatalog : IEventCatalog
{
    private readonly ConcurrentDictionary<Guid, Event> _events = new();

    public static readonly Event DefaultEvent = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "Rock Fest 2026",
        Guid.Parse("99999999-9999-9999-9999-999999999999"),
        "The Rockers",
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Estadio Nacional",
        DateTime.Parse("2026-11-20T20:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal),
        DateTime.Parse("2026-09-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal),
        DateTime.Parse("2026-11-20T19:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal));

    public InMemoryEventCatalog()
    {
        _events.TryAdd(DefaultEvent.Id, DefaultEvent);
    }

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken) =>
        Task.FromResult(_events.ContainsKey(eventId));
}
