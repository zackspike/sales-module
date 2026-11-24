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
    /// Returns the purchase that already paid with <paramref name="idempotencyKey"/> and the sales it made, or
    /// null if the key was never used. Read-only: it lets a replay be answered before the rules of a new
    /// payment (time left, held seats, token), which no longer hold once the purchase is paid.
    /// </summary>
    Task<PurchaseSaleRecord?> FindByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Sells the seats <paramref name="purchase"/> holds, at most once per <paramref name="idempotencyKey"/>:
    /// <list type="number">
    /// <item>If the key was already used, nothing is sold and nothing is called: the sales made with it are
    /// returned as <see cref="PurchaseSaleStatus.Replayed"/> when they belong to the same purchase, or
    /// <see cref="PurchaseSaleStatus.IdempotencyKeyConflict"/> otherwise.</item>
    /// <item>Otherwise, inside one transaction, every held seat of <see cref="PurchaseSession.EventId"/> is read.
    /// If any is missing or already sold, nothing is sold, <paramref name="capture"/> is not called and
    /// <see cref="PurchaseSaleStatus.SeatsUnavailable"/> lists every one of them, not just the first.</item>
    /// <item>Otherwise every seat is sold with <paramref name="sell"/> and stored with its ticket and the key,
    /// and <paramref name="capture"/> runs with the sales, still inside the transaction.</item>
    /// <item>The transaction commits only if the capture is <see cref="PaymentCallStatus.Succeeded"/>
    /// (contract 1.5, ADR-003); otherwise it rolls back and <see cref="PurchaseSaleStatus.NotCaptured"/> carries
    /// the capture's status.</item>
    /// </list>
    /// An exception thrown by <paramref name="sell"/> or <paramref name="capture"/> rolls back and propagates.
    /// Concurrent calls for the same seats can't both sell them.
    /// </summary>
    Task<PurchaseSaleOutcome> SellOnceAsync(
        Guid idempotencyKey,
        PurchaseSession purchase,
        Func<EventSeat, Ticket> sell,
        Func<IReadOnlyList<EventSeatSale>, CancellationToken, Task<PaymentCaptureResult>> capture,
        CancellationToken cancellationToken);
}

public enum PurchaseSaleStatus
{
    /// <summary>The seats were sold, the payment captured and the transaction committed.</summary>
    Sold,

    /// <summary>The key was already used by this purchase; its sales are returned and nothing changed.</summary>
    Replayed,

    /// <summary>The key was already used by another purchase; nothing changed.</summary>
    IdempotencyKeyConflict,

    /// <summary>Some held seats are missing or already sold (see <see cref="PurchaseSaleOutcome.FailedSeats"/>); nothing was sold.</summary>
    SeatsUnavailable,

    /// <summary>The capture did not succeed (see <see cref="PurchaseSaleOutcome.CaptureStatus"/>); the transaction rolled back.</summary>
    NotCaptured
}

/// <summary>
/// Why a held seat could not be sold.
/// </summary>
public enum SeatSaleFailure
{
    /// <summary>The seat does not exist in the purchase's event (SEAT_NOT_FOUND).</summary>
    NotFound,

    /// <summary>The seat was already sold (SEAT_ALREADY_SOLD).</summary>
    AlreadySold
}

/// <summary>
/// A held seat that could not be sold, and why.
/// </summary>
public sealed record FailedEventSeat(Guid EventSeatId, SeatSaleFailure Reason);

/// <summary>
/// Result of <see cref="IPurchaseSaleRepository.SellOnceAsync"/>. <see cref="Sales"/> is set only for
/// <see cref="PurchaseSaleStatus.Sold"/> and <see cref="PurchaseSaleStatus.Replayed"/>, <see cref="FailedSeats"/>
/// only for <see cref="PurchaseSaleStatus.SeatsUnavailable"/>, and <see cref="CaptureStatus"/> only for
/// <see cref="PurchaseSaleStatus.NotCaptured"/>.
/// </summary>
public sealed record PurchaseSaleOutcome(
    PurchaseSaleStatus Status,
    IReadOnlyList<EventSeatSale> Sales,
    IReadOnlyList<FailedEventSeat> FailedSeats,
    PaymentCallStatus? CaptureStatus)
{
    public static PurchaseSaleOutcome Sold(IReadOnlyList<EventSeatSale> sales) =>
        new(PurchaseSaleStatus.Sold, sales, [], null);

    public static PurchaseSaleOutcome Replayed(IReadOnlyList<EventSeatSale> sales) =>
        new(PurchaseSaleStatus.Replayed, sales, [], null);

    public static PurchaseSaleOutcome IdempotencyKeyConflict() =>
        new(PurchaseSaleStatus.IdempotencyKeyConflict, [], [], null);

    public static PurchaseSaleOutcome SeatsUnavailable(IReadOnlyList<FailedEventSeat> failedSeats) =>
        new(PurchaseSaleStatus.SeatsUnavailable, [], failedSeats, null);

    public static PurchaseSaleOutcome NotCaptured(PaymentCallStatus captureStatus) =>
        new(PurchaseSaleStatus.NotCaptured, [], [], captureStatus);
}

/// <summary>
/// The purchase that paid with an idempotency key, and the seats it bought with it.
/// </summary>
public sealed record PurchaseSaleRecord(Guid PurchaseId, IReadOnlyList<EventSeatSale> Sales);
