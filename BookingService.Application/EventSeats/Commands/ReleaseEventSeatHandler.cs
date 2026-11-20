using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;

namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Lets a fan give up the seat reserved with <see cref="ReserveEventSeatHandler"/> before the reservation expires.
/// Validates the email (400), checks the event and event seat exist (404) and the seat is not sold (409), and
/// removes the seat lock only if this buyer (email and buyer token) holds it (409 otherwise). Releasing a seat that is not reserved
/// succeeds, so retries and expired reservations need no special handling.
/// </summary>
public sealed class ReleaseEventSeatHandler
{
    private readonly IEventCatalog _eventCatalog;
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IEventSeatLockStore _eventSeatLocks;

    public ReleaseEventSeatHandler(
        IEventCatalog eventCatalog,
        IEventSeatRepository eventSeatRepository,
        IEventSeatLockStore eventSeatLocks)
    {
        _eventCatalog = eventCatalog;
        _eventSeatRepository = eventSeatRepository;
        _eventSeatLocks = eventSeatLocks;
    }

    public async Task<ReleaseEventSeatResult> HandleAsync(
        ReleaseEventSeatCommand command,
        CancellationToken cancellationToken)
    {
        var errors = TicketHolderValidator.ValidateEmail(command.Email);
        if (errors.Count > 0)
        {
            return ReleaseEventSeatResult.Invalid(errors);
        }

        if (!await _eventCatalog.ExistsAsync(command.EventId, cancellationToken))
        {
            return ReleaseEventSeatResult.Of(ReleaseEventSeatStatus.EventNotFound);
        }

        var eventSeat = await _eventSeatRepository.GetByIdAsync(command.EventId, command.EventSeatId, cancellationToken);
        if (eventSeat is null)
        {
            return ReleaseEventSeatResult.Of(ReleaseEventSeatStatus.EventSeatNotFound);
        }

        if (eventSeat.Status == EventSeatStatus.Sold)
        {
            return ReleaseEventSeatResult.Of(ReleaseEventSeatStatus.AlreadySold);
        }

        var holder = EventSeatLockHolder.From(command.Email, command.BuyerToken);
        var currentHolder = await _eventSeatLocks.GetHolderIdentifierAsync(eventSeat.Id, cancellationToken);
        if (currentHolder is not null && currentHolder != holder)
        {
            return ReleaseEventSeatResult.Of(ReleaseEventSeatStatus.ReservedByAnotherBuyer);
        }

        // Only removes the lock if it is still ours, so a lock taken by another fan in the meantime survives.
        await _eventSeatLocks.ReleaseAsync(eventSeat.Id, holder, cancellationToken);
        return ReleaseEventSeatResult.Of(ReleaseEventSeatStatus.Released);
    }
}
