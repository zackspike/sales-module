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
    public async Task<IReadOnlyList<AvailableEventSeat>?> HandleAsync(
        GetAvailableEventSeatsQuery query,
        CancellationToken cancellationToken)
    {
        // Only existing events are cached, so a hit skips the event check too.
        var eventSeats = await _availableEventSeatsCache.GetAsync(query.EventId, cancellationToken);
        if (eventSeats is null)
        {
            if (!await _eventCatalog.ExistsAsync(query.EventId, cancellationToken))
            {
                return null;
            }

            var available = (await _eventSeatRepository.GetByEventAsync(query.EventId, cancellationToken))
                .Where(avs => avs.Status == EventSeatStatus.Available)
                .ToList();

            var venueSeatIds = available.Select(avs => avs.VenueSeatId).ToList();
            var venueSeats = (await _venueSeatCatalog.GetByIdsAsync(venueSeatIds, cancellationToken))
                .ToDictionary(vs => vs.Id);

            eventSeats = available
                .Select(avs => new AvailableEventSeat(avs.Id, SeatNumberOf(avs, venueSeats)))
                .OrderBy(avs => avs.SeatNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
            await _availableEventSeatsCache.SetAsync(query.EventId, eventSeats, cancellationToken);
        }

        var reserved = await _eventSeatLocks.GetLockedEventSeatIdsAsync(
            eventSeats.Select(s => s.EventSeatId).ToList(),
            cancellationToken);

        return eventSeats.Where(s => !reserved.Contains(s.EventSeatId)).ToList();
    }

    private static string SeatNumberOf(EventSeat seat, IReadOnlyDictionary<Guid, VenueSeat> venueSeats) =>
        venueSeats.TryGetValue(seat.VenueSeatId, out var venueSeat)
            ? venueSeat.SeatNumber
            : throw new InvalidOperationException(
                $"Event seat {seat.Id} references venue seat {seat.VenueSeatId}, which is not in the catalog.");
}
