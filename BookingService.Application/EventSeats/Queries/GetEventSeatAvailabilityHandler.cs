using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.EventSeats;

namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Handles getting the availability of a specific event seat (SP-04 / APP-02).
/// The status is <c>Sold</c> or <c>Available</c> as stored in the repository, or
/// <see cref="EventSeatAvailabilityStatus.Reserved"/> when an available seat is locked by a fan.
/// </summary>
public sealed class GetEventSeatAvailabilityHandler
{
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IEventCatalog _eventCatalog;
    private readonly IEventSeatLockStore _eventSeatLocks;
    private readonly IVenueSeatCatalog _venueSeatCatalog;

    public GetEventSeatAvailabilityHandler(
        IEventSeatRepository eventSeatRepository,
        IEventCatalog eventCatalog,
        IEventSeatLockStore eventSeatLocks,
        IVenueSeatCatalog venueSeatCatalog)
    {
        _eventSeatRepository = eventSeatRepository;
        _eventCatalog = eventCatalog;
        _eventSeatLocks = eventSeatLocks;
        _venueSeatCatalog = venueSeatCatalog;
    }

    /// <summary>
    /// Returns the availability information for the seat, or null if the event or event seat is not found.
    /// </summary>
    public async Task<EventSeatAvailability?> HandleAsync(
        GetEventSeatAvailabilityQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _eventCatalog.ExistsAsync(query.EventId, cancellationToken))
        {
            return null;
        }

        var eventSeat = await _eventSeatRepository.GetByIdAsync(query.EventId, query.EventSeatId, cancellationToken);
        if (eventSeat is null)
        {
            return null;
        }

        var venueSeat = await _venueSeatCatalog.GetByIdAsync(eventSeat.VenueSeatId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Event seat {eventSeat.Id} references venue seat {eventSeat.VenueSeatId}, which is not in the catalog.");

        var status = eventSeat.Status switch
        {
            EventSeatStatus.Sold => EventSeatAvailabilityStatus.Sold,
            _ => await _eventSeatLocks.GetHolderIdentifierAsync(eventSeat.Id, cancellationToken) is not null
                ? EventSeatAvailabilityStatus.Reserved
                : EventSeatAvailabilityStatus.Available
        };

        return new EventSeatAvailability(eventSeat.Id, venueSeat.SeatNumber, status);
    }
}
