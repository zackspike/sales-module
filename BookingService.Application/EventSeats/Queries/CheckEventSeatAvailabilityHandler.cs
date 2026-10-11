using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.EventSeats;

namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Handles checking the availability of a specific event seat (SP-04 / APP-02).
/// The status is <c>Sold</c> or <c>Available</c> as stored in the repository, or
/// <see cref="EventSeatAvailabilityStatus.Reserved"/> when an available seat is locked by a fan.
/// </summary>
public sealed class CheckEventSeatAvailabilityHandler
{
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IEventCatalog _eventCatalog;
    private readonly IEventSeatLockStore _eventSeatLocks;
    private readonly IVenueSeatCatalog _venueSeatCatalog;

    public CheckEventSeatAvailabilityHandler(
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
    public EventSeatAvailability? Handle(CheckEventSeatAvailabilityQuery query)
    {
        if (!_eventCatalog.Exists(query.EventId))
        {
            return null;
        }

        var eventSeat = _eventSeatRepository.GetById(query.EventId, query.EventSeatId);
        if (eventSeat is null)
        {
            return null;
        }

        var venueSeat = _venueSeatCatalog.GetById(eventSeat.VenueSeatId)
            ?? throw new InvalidOperationException(
                $"Event seat {eventSeat.Id} references venue seat {eventSeat.VenueSeatId}, which is not in the catalog.");

        var status = eventSeat.Status switch
        {
            EventSeatStatus.Sold => EventSeatAvailabilityStatus.Sold,
            _ when _eventSeatLocks.GetHolderIdentifier(eventSeat.Id) is not null => EventSeatAvailabilityStatus.Reserved,
            _ => EventSeatAvailabilityStatus.Available
        };

        return new EventSeatAvailability(eventSeat.Id, venueSeat.SeatNumber, status);
    }
}
