using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Durable sale of every seat a purchase holds, in one transaction that commits only once Payment captured
/// the payment (ADR-003). Also keeps the idempotency key of every confirmed purchase, so a double click or a
/// network retry of "Pagar" never sells or charges twice.
/// </summary>
public interface IPurchaseSaleRepository
{
    /// <summary>
    /// Sells the seats <paramref name="purchase"/> holds, at most once per <paramref name="idempotencyKey"/>:
    /// <list type="number">
    /// <item>If the key was already used, nothing is sold and nothing is called: the sales made with it are
    /// returned as <see cref="PurchaseSaleStatus.Replayed"/> when they belong to the same purchase, or
    /// <see cref="PurchaseSaleStatus.IdempotencyKeyConflict"/> otherwise.</item>
    /// <item>Otherwise, inside one transaction, every held seat of <see cref="PurchaseSession.EventId"/> is read,
    /// sold with <paramref name="sell"/> and stored with its ticket and the key; then
    /// <paramref name="confirm"/> runs with the sales, still inside the transaction.</item>
    /// <item>The transaction commits only if <paramref name="confirm"/> returns true; otherwise it rolls back
    /// and <see cref="PurchaseSaleStatus.NotConfirmed"/> is returned with no sales.</item>
    /// </list>
    /// A missing seat or a seat already sold rolls back without calling <paramref name="confirm"/>. An exception
    /// thrown by <paramref name="sell"/> or <paramref name="confirm"/> rolls back and propagates.
    /// Concurrent calls for the same seats can't both sell them.
    /// </summary>
    Task<PurchaseSaleOutcome> SellOnceAsync(
        Guid idempotencyKey,
        PurchaseSession purchase,
        Func<EventSeat, Ticket> sell,
        Func<IReadOnlyList<EventSeatSale>, CancellationToken, Task<bool>> confirm,
        CancellationToken cancellationToken);
}

public enum PurchaseSaleStatus
{
    /// <summary>The seats were sold and the transaction committed.</summary>
    Sold,

    /// <summary>The key was already used by this purchase; its sales are returned and nothing changed.</summary>
    Replayed,

    /// <summary>The key was already used by another purchase; nothing changed.</summary>
    IdempotencyKeyConflict,

    /// <summary>A held seat does not exist in the purchase's event; nothing was sold.</summary>
    EventSeatNotFound,

    /// <summary>A held seat was already sold; nothing was sold.</summary>
    AlreadySold,

    /// <summary>The confirm callback returned false; the transaction rolled back.</summary>
    NotConfirmed
}

/// <summary>
/// Result of <see cref="IPurchaseSaleRepository.SellOnceAsync"/>. <see cref="Sales"/> is set for
/// <see cref="PurchaseSaleStatus.Sold"/> and <see cref="PurchaseSaleStatus.Replayed"/>, and empty otherwise.
/// </summary>
public sealed record PurchaseSaleOutcome(PurchaseSaleStatus Status, IReadOnlyList<EventSeatSale> Sales);
