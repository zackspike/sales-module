using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;

namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// Lets a fan give up a seat held by their purchase before the session expires: validates the session
/// (401), checks the event and event seat exist (404) and the seat is not sold (409), and removes the
/// lock only if this purchase holds it (409 otherwise). Releasing a seat the purchase does not hold
/// succeeds, so retries and expired holds need no special handling.
/// The session is read before PostgreSQL (ADR-002).
/// </summary>
public sealed class ReleaseHeldSeatHandler
{
    private readonly IPurchaseSessionStore _sessions;
    private readonly IEventCatalog _eventCatalog;
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IEventSeatLockStore _eventSeatLocks;

    public ReleaseHeldSeatHandler(
        IPurchaseSessionStore sessions,
        IEventCatalog eventCatalog,
        IEventSeatRepository eventSeatRepository,
        IEventSeatLockStore eventSeatLocks)
    {
        _sessions = sessions;
        _eventCatalog = eventCatalog;
        _eventSeatRepository = eventSeatRepository;
        _eventSeatLocks = eventSeatLocks;
    }

    public async Task<ReleaseHeldSeatResult> HandleAsync(
        ReleaseHeldSeatCommand command,
        CancellationToken cancellationToken)
    {
        // Redis before PostgreSQL (ADR-002).
        var purchase = await _sessions.GetAsync(command.PurchaseId, cancellationToken);
        if (purchase is null)
        {
            return ReleaseHeldSeatResult.Of(ReleaseHeldSeatStatus.SessionExpired);
        }

        if (purchase.IsExpired(DateTime.UtcNow))
        {
            await PurchaseSessionExpiration.ReleaseHeldSeatsAsync(purchase, _eventSeatLocks, cancellationToken);
            return ReleaseHeldSeatResult.Of(ReleaseHeldSeatStatus.SessionExpired);
        }

        if (!await _eventCatalog.ExistsAsync(command.EventId, cancellationToken))
        {
            return ReleaseHeldSeatResult.Of(ReleaseHeldSeatStatus.EventNotFound);
        }

        var eventSeat = await _eventSeatRepository.GetByIdAsync(command.EventId, command.EventSeatId, cancellationToken);
        if (eventSeat is null)
        {
            return ReleaseHeldSeatResult.Of(ReleaseHeldSeatStatus.EventSeatNotFound);
        }

        if (eventSeat.Status == EventSeatStatus.Sold)
        {
            return ReleaseHeldSeatResult.Of(ReleaseHeldSeatStatus.AlreadySold);
        }

        var holder = PurchaseSeatLockHolder.Of(purchase.Id);
        var currentHolder = await _eventSeatLocks.GetHolderIdentifierAsync(eventSeat.Id, cancellationToken);
        if (currentHolder is not null && currentHolder != holder)
        {
            return ReleaseHeldSeatResult.Of(ReleaseHeldSeatStatus.HeldByAnotherPurchase);
        }

        // Only removes the lock if it is still ours, so a lock taken by another purchase in the
        // meantime survives.
        await _eventSeatLocks.ReleaseAsync(eventSeat.Id, holder, cancellationToken);
        purchase.Release(eventSeat.Id);
        await _sessions.SaveAsync(purchase, cancellationToken);

        return ReleaseHeldSeatResult.Of(ReleaseHeldSeatStatus.Released);
    }
}
