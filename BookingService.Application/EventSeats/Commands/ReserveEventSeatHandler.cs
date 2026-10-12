using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.EventSeats;

namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// First step of the purchase flow: the fan selects a seat and clicks "comprar".
/// Validates name and email (400), checks the event and event seat exist (404) and the seat is not sold,
/// and reserves it for the buyer (identified by email) during <see cref="EventSeatReservationPolicy.Duration"/>.
/// </summary>
public sealed class ReserveEventSeatHandler
{
    private readonly IEventCatalog _eventCatalog;
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IVenueSeatCatalog _venueSeatCatalog;
    private readonly IEventSeatLockStore _eventSeatLocks;

    public ReserveEventSeatHandler(
        IEventCatalog eventCatalog,
        IEventSeatRepository eventSeatRepository,
        IVenueSeatCatalog venueSeatCatalog,
        IEventSeatLockStore eventSeatLocks)
    {
        _eventCatalog = eventCatalog;
        _eventSeatRepository = eventSeatRepository;
        _venueSeatCatalog = venueSeatCatalog;
        _eventSeatLocks = eventSeatLocks;
    }

    public ReserveEventSeatResult Handle(ReserveEventSeatCommand command)
    {
        var errors = TicketHolderValidator.Validate(command.FullName, command.Email);
        if (errors.Count > 0)
        {
            return ReserveEventSeatResult.Invalid(errors);
        }

        if (!_eventCatalog.Exists(command.EventId))
        {
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.EventNotFound);
        }

        var eventSeat = _eventSeatRepository.GetById(command.EventId, command.EventSeatId);
        if (eventSeat is null)
        {
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.EventSeatNotFound);
        }

        if (eventSeat.Status == EventSeatStatus.Sold)
        {
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.AlreadySold);
        }

        // Resolved before locking, so a seat with broken venue data is never left reserved.
        var seatNumber = SeatNumberOf(eventSeat);

        var holder = EventSeatLockHolder.From(command.Email);
        var expiresAtUtc = DateTime.UtcNow + EventSeatReservationPolicy.Duration;

        if (_eventSeatLocks.TryAcquire(eventSeat.Id, holder, EventSeatReservationPolicy.Duration) == EventSeatLockResult.HeldByAnother)
        {
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.ReservedByAnotherBuyer);
        }

        // The seat may have been sold (and its lock released) between the availability check
        // and the lock: re-check so a sold seat is never left reserved.
        var current = _eventSeatRepository.GetById(command.EventId, command.EventSeatId);
        if (current is null)
        {
            _eventSeatLocks.Release(eventSeat.Id, holder);
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.EventSeatNotFound);
        }

        if (current.Status == EventSeatStatus.Sold)
        {
            _eventSeatLocks.Release(eventSeat.Id, holder);
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.AlreadySold);
        }

        return ReserveEventSeatResult.Reserved(new ReservedEventSeat(current.Id, current.EventId, seatNumber, expiresAtUtc));
    }

    private string SeatNumberOf(EventSeat eventSeat) =>
        _venueSeatCatalog.GetById(eventSeat.VenueSeatId)?.SeatNumber
            ?? throw new InvalidOperationException(
                $"Event seat {eventSeat.Id} references venue seat {eventSeat.VenueSeatId}, which is not in the catalog.");
}
