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

    public async Task<ReserveEventSeatResult> HandleAsync(
        ReserveEventSeatCommand command,
        CancellationToken cancellationToken)
    {
        var errors = TicketHolderValidator.Validate(command.FullName, command.Email);
        if (errors.Count > 0)
        {
            return ReserveEventSeatResult.Invalid(errors);
        }

        if (!await _eventCatalog.ExistsAsync(command.EventId, cancellationToken))
        {
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.EventNotFound);
        }

        var eventSeat = await _eventSeatRepository.GetByIdAsync(command.EventId, command.EventSeatId, cancellationToken);
        if (eventSeat is null)
        {
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.EventSeatNotFound);
        }

        if (eventSeat.Status == EventSeatStatus.Sold)
        {
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.AlreadySold);
        }

        // Resolved before locking, so a seat with broken venue data is never left reserved.
        var seatNumber = await SeatNumberOfAsync(eventSeat, cancellationToken);

        var holder = EventSeatLockHolder.From(command.Email);
        var expiresAtUtc = DateTime.UtcNow + EventSeatReservationPolicy.Duration;

        var lockResult = await _eventSeatLocks.TryAcquireAsync(
            eventSeat.Id,
            holder,
            EventSeatReservationPolicy.Duration,
            cancellationToken);
        if (lockResult == EventSeatLockResult.HeldByAnother)
        {
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.ReservedByAnotherBuyer);
        }

        // The seat may have been sold (and its lock released) between the availability check
        // and the lock: re-check so a sold seat is never left reserved.
        var current = await _eventSeatRepository.GetByIdAsync(command.EventId, command.EventSeatId, cancellationToken);
        if (current is null)
        {
            await _eventSeatLocks.ReleaseAsync(eventSeat.Id, holder, cancellationToken);
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.EventSeatNotFound);
        }

        if (current.Status == EventSeatStatus.Sold)
        {
            await _eventSeatLocks.ReleaseAsync(eventSeat.Id, holder, cancellationToken);
            return ReserveEventSeatResult.Failed(ReserveEventSeatStatus.AlreadySold);
        }

        return ReserveEventSeatResult.Reserved(new ReservedEventSeat(current.Id, current.EventId, seatNumber, expiresAtUtc));
    }

    private async Task<string> SeatNumberOfAsync(EventSeat eventSeat, CancellationToken cancellationToken) =>
        (await _venueSeatCatalog.GetByIdAsync(eventSeat.VenueSeatId, cancellationToken))?.SeatNumber
            ?? throw new InvalidOperationException(
                $"Event seat {eventSeat.Id} references venue seat {eventSeat.VenueSeatId}, which is not in the catalog.");
}
