using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Venues;

namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Handles retrieving the available event seats of an event (SP-04 / APP-01). The seats available in the
/// repository are served from <see cref="IAvailableEventSeatsCache"/> when possible, and seats currently
/// reserved by a fan (seat lock) are left out.
/// </summary>
public sealed class GetAvailableEventSeatsHandler
{
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IEventCatalog _eventCatalog;
    private readonly IEventSeatLockStore _eventSeatLocks;
    private readonly IAvailableEventSeatsCache _availableEventSeatsCache;
    private readonly IVenueSeatCatalog _venueSeatCatalog;

    public GetAvailableEventSeatsHandler(
        IEventSeatRepository eventSeatRepository,
        IEventCatalog eventCatalog,
        IAvailableEventSeatsCache availableEventSeatsCache,
        IEventSeatLockStore eventSeatLockStore,
        IVenueSeatCatalog venueSeatCatalog)
    {
        _eventSeatRepository = eventSeatRepository;
        _eventCatalog = eventCatalog;
        _availableEventSeatsCache = availableEventSeatsCache;
        _eventSeatLocks = eventSeatLockStore;
        _venueSeatCatalog = venueSeatCatalog;
    }

    /// <summary>
    /// Returns the available event seats of the event sorted by seat number, or null if the event does not exist.
    /// </summary>
    public IReadOnlyList<AvailableEventSeat>? Handle(GetAvailableEventSeatsQuery query)
    {
        // Only existing events are cached, so a hit skips the event check too.
        var eventSeats = _availableEventSeatsCache.Get(query.EventId);
        if (eventSeats is null)
        {
            if (!_eventCatalog.Exists(query.EventId))
            {
                return null;
            }

            var available = _eventSeatRepository.GetByEvent(query.EventId)
                .Where(avs => avs.Status == EventSeatStatus.Available)
                .ToList();

            var venueSeatIds = available.Select(avs => avs.VenueSeatId).ToList();
            var venueSeats = _venueSeatCatalog.GetByIds(venueSeatIds).ToDictionary(vs => vs.Id);

            eventSeats = available
                .Select(avs => new AvailableEventSeat(avs.Id, SeatNumberOf(avs, venueSeats)))
                .OrderBy(avs => avs.SeatNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _availableEventSeatsCache.Set(query.EventId, eventSeats);
        }

        var reserved = _eventSeatLocks.GetLockedEventSeatIds(eventSeats.Select(s => s.EventSeatId).ToList());

        return eventSeats.Where(s => !reserved.Contains(s.EventSeatId)).ToList();
    }

    private static string SeatNumberOf(EventSeat seat, IReadOnlyDictionary<Guid, VenueSeat> venueSeats) =>
        venueSeats.TryGetValue(seat.VenueSeatId, out var venueSeat)
            ? venueSeat.SeatNumber
            : throw new InvalidOperationException(
                $"Event seat {seat.Id} references venue seat {seat.VenueSeatId}, which is not in the catalog.");
}
