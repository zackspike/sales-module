using BookingService.Application.Abstractions;
using BookingService.Application.Purchases.ReadModels;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;

namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// Holds one event seat for a purchase until its session expires: validates the session (401), checks the
/// event and event seat exist (404) and the seat is neither sold (409) nor held by another purchase (409),
/// and refuses to go past the purchase's seat limit (409) without holding anything.
/// The session is read before PostgreSQL (ADR-002) and the hold inherits the session's deadline, so
/// holding a seat never buys more time (ADR-001).
/// </summary>
public sealed class HoldSeatHandler
{
    private readonly IPurchaseSessionStore _sessions;
    private readonly IEventCatalog _eventCatalog;
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IVenueSeatCatalog _venueSeatCatalog;
    private readonly IEventSeatLockStore _eventSeatLocks;
    private readonly PurchaseSessionOptions _options;

    public HoldSeatHandler(
        IPurchaseSessionStore sessions,
        IEventCatalog eventCatalog,
        IEventSeatRepository eventSeatRepository,
        IVenueSeatCatalog venueSeatCatalog,
        IEventSeatLockStore eventSeatLocks,
        PurchaseSessionOptions options)
    {
        _sessions = sessions;
        _eventCatalog = eventCatalog;
        _eventSeatRepository = eventSeatRepository;
        _venueSeatCatalog = venueSeatCatalog;
        _eventSeatLocks = eventSeatLocks;
        _options = options;
    }

    public async Task<HoldSeatResult> HandleAsync(HoldSeatCommand command, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        // Redis before PostgreSQL (ADR-002): a dead session costs nothing to reject.
        var purchase = await _sessions.GetAsync(command.PurchaseId, cancellationToken);
        if (purchase is null)
        {
            return HoldSeatResult.Failed(HoldSeatStatus.SessionExpired);
        }

        if (purchase.IsExpired(nowUtc))
        {
            await PurchaseSessionExpiration.ReleaseHeldSeatsAsync(purchase, _eventSeatLocks, cancellationToken);
            return HoldSeatResult.Failed(HoldSeatStatus.SessionExpired);
        }

        if (!await _eventCatalog.ExistsAsync(command.EventId, cancellationToken))
        {
            return HoldSeatResult.Failed(HoldSeatStatus.EventNotFound);
        }

        var eventSeat = await _eventSeatRepository.GetByIdAsync(command.EventId, command.EventSeatId, cancellationToken);
        if (eventSeat is null)
        {
            return HoldSeatResult.Failed(HoldSeatStatus.EventSeatNotFound);
        }

        if (eventSeat.Status == EventSeatStatus.Sold)
        {
            return HoldSeatResult.Failed(HoldSeatStatus.AlreadySold);
        }

        // Resolved before locking, so a seat with broken venue data is never left held.
        var seatNumber = await SeatNumberOfAsync(eventSeat, cancellationToken);

        try
        {
            // Not wrapped for PurchaseExpiredException: the same nowUtc already passed IsExpired above.
            purchase.Hold(command.EventId, command.EventSeatId, _options.MaxSeats, nowUtc);
        }
        catch (SeatLimitReachedException)
        {
            return HoldSeatResult.Failed(HoldSeatStatus.SeatLimitReached);
        }
        // A session is opened for a single event: the payment token covers one event's seats. The rule
        // lives in PurchaseSession.Hold; the filter keeps a misconfigured MaxSeats (ArgumentOutOfRangeException)
        // a 500.
        catch (ArgumentException ex) when (ex.ParamName == "eventId")
        {
            return HoldSeatResult.Invalid(new Dictionary<string, string[]>
            {
                ["eventId"] = ["The purchase session was opened for another event."]
            });
        }

        var holder = PurchaseSeatLockHolder.Of(purchase.Id);

        // The hold lasts exactly as long as the session has left, never a fresh window (ADR-001).
        // Re-holding a seat this purchase already has renews the lock to the same deadline, so it is a no-op.
        var lockResult = await _eventSeatLocks.TryAcquireAsync(
            command.EventSeatId,
            holder,
            purchase.ExpiresAtUtc - nowUtc,
            cancellationToken);
        if (lockResult == EventSeatLockResult.HeldByAnother)
        {
            return await DropHeldSeatAsync(purchase, command.EventSeatId, HoldSeatStatus.HeldByAnotherPurchase, cancellationToken);
        }

        // The seat may have been sold (and its lock released) between the availability check and the
        // lock: re-read so a sold seat is never left held.
        var current = await _eventSeatRepository.GetByIdAsync(command.EventId, command.EventSeatId, cancellationToken);
        if (current is null || current.Status == EventSeatStatus.Sold)
        {
            await _eventSeatLocks.ReleaseAsync(command.EventSeatId, holder, cancellationToken);
            var status = current is null ? HoldSeatStatus.EventSeatNotFound : HoldSeatStatus.AlreadySold;
            return await DropHeldSeatAsync(purchase, command.EventSeatId, status, cancellationToken);
        }

        await _sessions.SaveAsync(purchase, cancellationToken);

        return HoldSeatResult.Held(new HeldEventSeat(current.Id, current.EventId, seatNumber, purchase.ExpiresAtUtc));
    }

    /// <summary>
    /// Takes the seat back out of the purchase and stores the session, so a seat this purchase could not
    /// keep is not left in it. The seats it already held stay held: one conflicting seat does not undo
    /// the rest of the selection.
    /// </summary>
    private async Task<HoldSeatResult> DropHeldSeatAsync(
        PurchaseSession purchase,
        Guid eventSeatId,
        HoldSeatStatus status,
        CancellationToken cancellationToken)
    {
        purchase.Release(eventSeatId);
        await _sessions.SaveAsync(purchase, cancellationToken);
        return HoldSeatResult.Failed(status);
    }

    private async Task<string> SeatNumberOfAsync(EventSeat eventSeat, CancellationToken cancellationToken) =>
        (await _venueSeatCatalog.GetByIdAsync(eventSeat.VenueSeatId, cancellationToken))?.SeatNumber
            ?? throw new InvalidOperationException(
                $"Event seat {eventSeat.Id} references venue seat {eventSeat.VenueSeatId}, which is not in the catalog.");
}
