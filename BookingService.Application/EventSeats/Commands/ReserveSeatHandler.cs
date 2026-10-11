using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.EventSeats;

namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// First step of the purchase flow: the fan selects a seat and clicks "comprar".
/// Validates name, email and event (400/404), checks the event seat is not sold, and locks it for the
/// buyer (identified by email) during <see cref="EventSeatReservationPolicy.LockDuration"/>.
/// </summary>
public sealed class ReserveSeatHandler
{
    private readonly SeatPurchaseValidator _validator;
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IVenueSeatCatalog _venueSeatCatalog;
    private readonly IEventSeatLockStore _eventSeatLocks;

    public ReserveSeatHandler(
        SeatPurchaseValidator validator,
        IEventSeatRepository eventSeatRepository,
        IVenueSeatCatalog venueSeatCatalog,
        IEventSeatLockStore eventSeatLocks)
    {
        _validator = validator;
        _eventSeatRepository = eventSeatRepository;
        _venueSeatCatalog = venueSeatCatalog;
        _eventSeatLocks = eventSeatLocks;
    }

    public ReserveSeatResult Handle(ReserveSeatCommand command)
    {
        var validation = _validator.Validate(command.EventId, command.FullName, command.Email);

        if (validation.Status == SeatPurchaseValidationStatus.Invalid)
        {
            return ReserveSeatResult.Invalid(validation.Errors);
        }

        if (validation.Status == SeatPurchaseValidationStatus.EventNotFound)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.EventNotFound);
        }

        var eventSeat = _eventSeatRepository.GetById(command.EventId, command.EventSeatId);
        if (eventSeat is null)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.EventSeatNotFound);
        }

        if (eventSeat.Status == EventSeatStatus.Sold)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.AlreadySold);
        }

        // Resolved before locking, so a seat with broken venue data is never left reserved.
        var seatNumber = SeatNumberOf(eventSeat);

        var holder = SeatLockHolder.From(command.Email!);
        var expiresAtUtc = DateTime.UtcNow + EventSeatReservationPolicy.LockDuration;

        if (_eventSeatLocks.TryAcquire(eventSeat.Id, holder, EventSeatReservationPolicy.LockDuration) == SeatLockResult.HeldByAnother)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.LockedByAnotherBuyer);
        }

        // The seat may have been sold (and its lock released) between the availability check
        // and the lock: re-check so a sold seat is never left reserved.
        var current = _eventSeatRepository.GetById(command.EventId, command.EventSeatId);
        if (current is null || current.Status == EventSeatStatus.Sold)
        {
            _eventSeatLocks.Release(eventSeat.Id, holder);
            return ReserveSeatResult.Failed(ReserveSeatStatus.AlreadySold);
        }

        return ReserveSeatResult.Reserved(new ReservedSeat(current.Id, current.EventId, seatNumber, expiresAtUtc));
    }

    private string SeatNumberOf(EventSeat eventSeat) =>
        _venueSeatCatalog.GetById(eventSeat.VenueSeatId)?.SeatNumber
            ?? throw new InvalidOperationException(
                $"Event seat {eventSeat.Id} references venue seat {eventSeat.VenueSeatId}, which is not in the catalog.");
}

/// <summary>
/// The buyer identity stored in a seat lock: the email, trimmed and case-insensitive.
/// </summary>
internal static class SeatLockHolder
{
    public static string From(string email) => email.Trim().ToLowerInvariant();
}
