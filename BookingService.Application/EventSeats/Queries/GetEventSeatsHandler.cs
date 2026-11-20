using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Venues;

namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Handles listing all event seats of an event with their availability (SP-04 / APP-01). The seats and their
/// stored status are served from <see cref="IEventSeatsCache"/> when possible, and seats that are not sold but
/// currently locked by a fan are reported as <see cref="EventSeatAvailabilityStatus.Reserved"/>.
/// </summary>
public sealed class GetEventSeatsHandler
{
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IEventCatalog _eventCatalog;
    private readonly IEventSeatLockStore _eventSeatLocks;
    private readonly IEventSeatsCache _eventSeatsCache;
    private readonly IVenueSeatCatalog _venueSeatCatalog;

    public GetEventSeatsHandler(
        IEventSeatRepository eventSeatRepository,
        IEventCatalog eventCatalog,
        IEventSeatsCache eventSeatsCache,
        IEventSeatLockStore eventSeatLockStore,
        IVenueSeatCatalog venueSeatCatalog)
    {
        _eventSeatRepository = eventSeatRepository;
        _eventCatalog = eventCatalog;
        _eventSeatsCache = eventSeatsCache;
        _eventSeatLocks = eventSeatLockStore;
        _venueSeatCatalog = venueSeatCatalog;
    }

    /// <summary>
    /// Returns all event seats of the event sorted by seat number, or null if the event does not exist.
    /// </summary>
    public async Task<IReadOnlyList<EventSeatAvailability>?> HandleAsync(
        GetEventSeatsQuery query,
        CancellationToken cancellationToken)
    {
        // Only existing events are cached, so a hit skips the event check too.
        var eventSeats = await _eventSeatsCache.GetAsync(query.EventId, cancellationToken);
        if (eventSeats is null)
        {
            if (!await _eventCatalog.ExistsAsync(query.EventId, cancellationToken))
            {
                return null;
            }

            var stored = await _eventSeatRepository.GetByEventAsync(query.EventId, cancellationToken);

            var venueSeatIds = stored.Select(es => es.VenueSeatId).ToList();
            var venueSeats = (await _venueSeatCatalog.GetByIdsAsync(venueSeatIds, cancellationToken))
                .ToDictionary(vs => vs.Id);

            eventSeats = stored
                .Select(es => new EventSeatAvailability(es.Id, SeatNumberOf(es, venueSeats), StoredStatusOf(es)))
                .OrderBy(es => es.SeatNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
            await _eventSeatsCache.SetAsync(query.EventId, eventSeats, cancellationToken);
        }

        var reserved = await _eventSeatLocks.GetLockedEventSeatIdsAsync(
            eventSeats.Where(s => s.Status == EventSeatAvailabilityStatus.Available).Select(s => s.EventSeatId).ToList(),
            cancellationToken);

        return eventSeats
            .Select(s => reserved.Contains(s.EventSeatId) ? s with { Status = EventSeatAvailabilityStatus.Reserved } : s)
            .ToList();
    }

    private static EventSeatAvailabilityStatus StoredStatusOf(EventSeat seat) => seat.Status switch
    {
        EventSeatStatus.Sold => EventSeatAvailabilityStatus.Sold,
        _ => EventSeatAvailabilityStatus.Available
    };

    private static string SeatNumberOf(EventSeat seat, IReadOnlyDictionary<Guid, VenueSeat> venueSeats) =>
        venueSeats.TryGetValue(seat.VenueSeatId, out var venueSeat)
            ? venueSeat.SeatNumber
            : throw new InvalidOperationException(
                $"Event seat {seat.Id} references venue seat {seat.VenueSeatId}, which is not in the catalog.");
}
